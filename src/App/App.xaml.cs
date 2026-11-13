using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using NEO.Core;
using NEO.Infrastructure;
using NEO.Platform;
using NEO.ViewModels;
using NEO.Views;
using Forms = System.Windows.Forms;

namespace NEO;

public partial class App : Application
{
    static App()
    {
        // Fluent controls remain enabled. Automatic DWM backdrop would fill the
        // transparent orb HWND with a rectangular surface.
        AppContext.SetSwitch("Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop", true);
    }

    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEO");
    private SettingsStore store = null!;
    private NEOSettings settings = new();
    private NotchWindow notch = null!;
    private ConversationWindow conversation = null!;
    private bool conversationPositioned;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private IDisposable? shortcut;
    private DispatcherTimer? fullscreenTimer;
    private DispatcherTimer? recoveryTimer;
    private bool recovering;
    private ILlamaServerManager serverManager = null!;
    private Task serverOperation = Task.CompletedTask;
    private bool exiting;
    private bool voiceEnabled;
    private AssistantViewModel viewModel = null!;
    private IVoiceSession? voiceSession;
    private string? voiceEndpoint;
    private string? microphoneDeviceId;
    private bool userHidden;
    private Mutex? instance;
    private EventWaitHandle? exitSignal;
    private RegisteredWaitHandle? exitRegistration;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Array.IndexOf(e.Args, "--quit") >= 0)
        {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\NEO.Exit"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        NEOLog.Start(Environment.GetEnvironmentVariable("NEO_LOG_PATH") ?? Path.Combine(dataDirectory, "NEO.log"));
        NEOLog.Write("app.start", new { version = typeof(App).Assembly.GetName().Version?.ToString(), os = Environment.OSVersion.ToString() });
        instance = new Mutex(true, @"Local\NEO.Desktop", out bool first);
        if (!first) { NEOLog.Write("app.already_running"); Shutdown(); return; }
        exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\NEO.Exit");
        exitRegistration = ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => Dispatcher.InvokeAsync(() => _ = ExitSafelyAsync()), null, Timeout.Infinite, false);
        Directory.CreateDirectory(dataDirectory);
        DispatcherUnhandledException += OnUnhandled;
        store = new SettingsStore(Path.Combine(dataDirectory, "NEO.settings.json"));
        serverManager = new LlamaServerManager(http, Path.Combine(AppContext.BaseDirectory, "NEO.ServerHost.exe"), dataDirectory);
        serverManager.StateChanged += state => Dispatcher.InvokeAsync(() => viewModel?.SetServerState(state));
        notch = new NotchWindow(); conversation = new ConversationWindow(); MainWindow = notch;
        foreach (Window window in new Window[] { notch, conversation })
        {
            window.LocationChanged += (_, _) => LogGeometry(window);
            window.SizeChanged += (_, _) => LogGeometry(window);
            window.IsVisibleChanged += (_, _) => NEOLog.Write("ui.visibility", new { window = window.GetType().Name, window.IsVisible });
        }
        notch.PanelRequested += () => { if (conversation.IsVisible && conversation.WindowState != WindowState.Minimized) conversation.Hide(); else ShowPanel(); };
        conversation.SettingsRequested += ShowSettings;
        conversation.VoiceRequested += ToggleVoice;
        notch.SourceInitialized += (_, _) => WindowPlatform.Initialize(notch, passive: false);
        conversation.SourceInitialized += (_, _) => WindowPlatform.Initialize(conversation, passive: false);
        notch.Show();
        NEOLog.Write("ui.fluent.resources", new { foreground = notch.TryFindResource("TextFillColorPrimary")?.ToString(), secondary = notch.TryFindResource("TextFillColorSecondary")?.ToString(), card = notch.TryFindResource("ControlFillColorDefault")?.ToString(), systemIcons = new System.Windows.Media.FontFamily("Segoe Fluent Icons").Source });
        // Construct the visible shell before reading configuration or contacting the server.
        string? configError = null;
        try { settings = await store.LoadAsync(lifetime.Token); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException or UnauthorizedAccessException)
        { configError = "تعذّر تحميل الإعدادات. سيستخدم نيو الإعدادات الافتراضية."; Log(ex); }
        ApplySettings();
        voiceEnabled = settings.AutoStartVoice;
        if (Array.IndexOf(e.Args, "--expanded") >= 0) ShowPanel();
        if (Array.IndexOf(e.Args, "--settings") >= 0) ShowSettings();
        CreateTray();
        try { shortcut = WindowPlatform.RegisterShortcut(notch, ShowPanel); }
        catch (InvalidOperationException ex) { Log(ex); tray!.ShowBalloonTip(5000, "اختصار نيو", "الاختصار مستخدم من تطبيق آخر. يمكنك فتح نيو من أيقونته بجوار الساعة.", Forms.ToolTipIcon.Warning); }
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        fullscreenTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateVisibility(), Dispatcher);
        recoveryTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, async (_, _) =>
        {
            if (recovering || lifetime.IsCancellationRequested || viewModel.IsChecking || viewModel.IsSending) return;
            recovering = true;
            try
            {
                await CheckSafelyAsync();
                if (voiceEnabled && serverManager.State == LlamaServerState.Ready && voiceSession?.State is VoiceSessionState.Disconnected or VoiceSessionState.Error) await StartLiveSafelyAsync(revealErrors: false);
            }
            finally { recovering = false; }
        }, Dispatcher);
        if (configError != null) MessageBox.Show(configError, "إعدادات نيو", MessageBoxButton.OK, MessageBoxImage.Warning);
        await CheckSafelyAsync();
        if (settings.AutoStartVoice && serverManager.State == LlamaServerState.Ready) await StartLiveSafelyAsync();
    }

    private void ApplySettings()
    {
        NEOLog.Write("settings.apply", new { settings.HttpEndpoint, settings.RealtimeEndpoint, settings.Width, settings.Height, settings.Monitor });
        var endpoint = new Uri(settings.HttpEndpoint);
        if (voiceSession == null || !string.Equals(voiceEndpoint, settings.RealtimeEndpoint, StringComparison.OrdinalIgnoreCase) || microphoneDeviceId != settings.MicrophoneDeviceId)
        {
            var old = voiceSession;
            voiceSession = new LlamaRealtimeVoiceSession(new Uri(settings.RealtimeEndpoint), settings.MicrophoneDeviceId);
            voiceEndpoint = settings.RealtimeEndpoint;
            microphoneDeviceId = settings.MicrophoneDeviceId;
            if (old != null) _ = old.DisposeAsync();
        }
        viewModel?.Dispose();
        viewModel = new AssistantViewModel(new LlamaHealthClient(http, endpoint), new LlamaChatClient(http, endpoint), voiceSession, lifetime.Token);
        notch.DataContext = conversation.DataContext = viewModel;
        viewModel.SetServerState(serverManager.State);
        notch.Opacity = 1;
        notch.Topmost = settings.AlwaysOnTop;
        conversation.Topmost = false;
        WindowPlatform.Position(notch, settings, bottom: false);
    }
    private void CreateTray()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Resources/NEO.ico")).Stream;
        trayIcon = new System.Drawing.Icon(stream);
        var menu = new Forms.ContextMenuStrip();
        menu.RightToLeft = Forms.RightToLeft.Yes;
        menu.Items.Add("فتح نيو", null, (_, _) => Dispatcher.Invoke(ShowPanel));
        menu.Items.Add("إظهار أو إخفاء نيو", null, (_, _) => Dispatcher.Invoke(() => { userHidden = !userHidden; UpdateVisibility(); }));
        menu.Items.Add("إعادة الاتصال", null, (_, _) => Dispatcher.Invoke(Retry));
        menu.Items.Add("إعدادات نيو", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("إنهاء نيو", null, (_, _) => Dispatcher.Invoke(() => _ = ExitSafelyAsync()));
        tray = new Forms.NotifyIcon { Text = "نيو", Icon = trayIcon, Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowPanel);
    }
    private void ShowPanel()
    {
        NEOLog.Write("ui.panel.open");
        userHidden = false; UpdateVisibility();
        conversation.Reveal();
        if (!conversationPositioned)
        {
            WindowPlatform.Position(conversation, settings with { AlwaysOnTop = false }, bottom: false, center: true);
            conversationPositioned = true;
        }
    }
    private static void LogGeometry(Window window) => NEOLog.Write("ui.geometry", new
    {
        window = window.GetType().Name, window.Left, window.Top, window.ActualWidth, window.ActualHeight,
        dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window).PixelsPerInchX
    });
    private async void ToggleVoice()
    {
        NEOLog.Write("ui.notch.voice_toggle", new { viewModel.VoiceState });
        try
        {
            voiceEnabled = !viewModel.IsVoiceActive;
            if (!voiceEnabled) await viewModel.StopVoiceAsync();
            else { await CheckSafelyAsync(); if (serverManager.State == LlamaServerState.Ready) await viewModel.StartVoiceAsync(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Log(ex); }
    }
    private void ShowSettings()
    {
        NEOLog.Write("ui.settings.open");
        ShowPanel();
        conversation.ShowSettings(new SettingsView(settings, async updated =>
        {
            await store.SaveAsync(updated, lifetime.Token); settings = updated;
            if (!settings.AutoManageServer) await serverManager.StopAsync(lifetime.Token);
            ApplySettings(); await CheckSafelyAsync();
            voiceEnabled = settings.AutoStartVoice;
            if (settings.AutoStartVoice && serverManager.State == LlamaServerState.Ready) await StartLiveSafelyAsync();
            else await viewModel.StopVoiceAsync();
        }));
    }
    private async void Retry() => await CheckSafelyAsync();
    private async Task StartLiveSafelyAsync(bool revealErrors = true)
    {
        try { await viewModel.StartVoiceAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Log(ex); if (revealErrors) ShowPanel(); }
    }
    private async Task CheckSafelyAsync()
    {
        if (!serverOperation.IsCompleted) { await serverOperation; return; }
        serverOperation = CheckServerAndHealthAsync();
        await serverOperation;
    }
    private async Task CheckServerAndHealthAsync()
    {
        NEOLog.Write("health.start");
        try
        {
            var current = viewModel;
            await serverManager.EnsureReadyAsync(settings, lifetime.Token);
            current.SetServerState(serverManager.State);
            await current.CheckAsync(lifetime.Token);
            NEOLog.Write("health.complete", new { current.Status, current.Message });
            if (tray != null && current == viewModel) tray.Text = "نيو · " + viewModel.Status;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Log(ex); viewModel.SetServerState(LlamaServerState.Error); }
    }
    private async Task ExitSafelyAsync()
    {
        if (exiting) return;
        exiting = true; lifetime.Cancel(); recoveryTimer?.Stop();
        try
        {
            await serverOperation;
            if (voiceSession != null) await voiceSession.DisposeAsync();
            voiceSession = null;
            await serverManager.DisposeAsync();
        }
        catch (Exception ex) { Log(ex); }
        finally { Shutdown(); }
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        WindowPlatform.Position(notch, settings, false);
        if (conversation.IsVisible) WindowPlatform.Position(conversation, settings with { AlwaysOnTop = false }, false, center: true);
    });
    private void UpdateVisibility()
    {
        bool hide = userHidden || (settings.HideInFullscreen && WindowPlatform.IsFullscreenOtherApp(notch));
        if (hide) { notch.Hide(); conversation.Hide(); }
        else if (!notch.IsVisible) notch.Show();
    }
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show("حدث خطأ غير متوقع وسيُغلق نيو. تفاصيل الخطأ محفوظة في السجل.", "NEO", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; Shutdown(1);
    }
    private void Log(Exception exception)
    {
        NEOLog.Error("app.error", exception);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        NEOLog.Write("app.exit", new { e.ApplicationExitCode });
        lifetime.Cancel(); fullscreenTimer?.Stop(); recoveryTimer?.Stop();
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        shortcut?.Dispose();
        exitRegistration?.Unregister(null); exitSignal?.Dispose();
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        trayIcon?.Dispose(); conversation?.CloseForExit(); notch?.Close();
        try { voiceSession?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception ex) { Log(ex); }
        try { serverManager?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception ex) { Log(ex); }
        http.Dispose(); lifetime.Dispose(); instance?.Dispose();
        NEOLog.StopAsync().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
