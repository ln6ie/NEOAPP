using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Linq;
using NEO.Core;
using NEO.Infrastructure;

namespace NEO.ViewModels;

public sealed class AssistantViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IHealthClient health;
    private readonly IChatClient chat;
    private readonly IVoiceSession voice;
    private readonly CancellationToken lifetime;
    private readonly AssistantStateMachine machine = new();
    private CancellationTokenSource? requestCancellation;
    private string message = "جارٍ الاتصال…";
    private string inputText = string.Empty;
    private bool isSending;
    private bool serverAvailable;
    private LlamaServerState serverState;
    public string ServerStatus => serverState switch { LlamaServerState.Loading => "جارٍ تحميل النموذج…", LlamaServerState.Ready => "جاهز", LlamaServerState.Error => "تعذّر تشغيل الخادم؛ راجع السجل", _ => "غير متصل" };
    public void SetServerState(LlamaServerState state)
    {
        serverState = state;
        serverAvailable = state == LlamaServerState.Ready;
        Notify(nameof(ServerStatus)); Notify(nameof(IsServerConnected)); Notify(nameof(ConnectionBrush)); Notify(nameof(ConnectionLabel));
    }
    public bool IsServerConnected => serverAvailable || VoiceState is VoiceSessionState.Ready or VoiceSessionState.Listening or VoiceSessionState.Processing or VoiceSessionState.Speaking;
    public Brush ConnectionBrush => IsServerConnected ? Brushes.MediumSeaGreen : Brushes.IndianRed;
    public string ConnectionLabel => ServerStatus;
    public bool IsSpeaking => VoiceState == VoiceSessionState.Speaking;
    public bool IsProcessing => VoiceState == VoiceSessionState.Processing;
    private string liveResponse = string.Empty;
    public string LiveResponse { get => liveResponse; private set { liveResponse = value; Notify(); } }
    public string MicrophoneGlyph => IsVoiceActive ? "\uE720" : "\uE198";

    public AssistantViewModel(IHealthClient health, IChatClient chat, IVoiceSession voice, CancellationToken lifetime)
    {
        this.health = health;
        this.chat = chat;
        this.voice = voice;
        this.lifetime = lifetime;
        voice.StateChanged += OnVoiceStateChanged;
        voice.AudioLevelChanged += OnAudioLevelChanged;
        voice.TextDelta += OnVoiceTextDelta;
        voice.ResponseCompleted += OnVoiceResponseCompleted;
    }

    public AssistantState State => machine.State;
    public string Status => IsVoiceActive ? "مباشر · " + VoiceStatus : State switch
    {
        AssistantState.Connecting => "جارٍ الاتصال",
        AssistantState.Ready => "متصل",
        AssistantState.Processing => "جارٍ التفكير",
        AssistantState.Error => "تعذّر الاتصال",
        AssistantState.Disconnected => "غير متصل",
        _ => State.ToString()
    };
    public string Message { get => message; private set { message = value; Notify(); } }
    public string InputText { get => inputText; set { inputText = value; Notify(); Notify(nameof(CanSend)); } }
    public bool IsChecking => State == AssistantState.Connecting;
    public bool IsSending { get => isSending; private set { isSending = value; Notify(); Notify(nameof(CanSend)); } }
    public bool CanSend => State is AssistantState.Ready or AssistantState.Error && !IsSending && !isAttaching && (!string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0);
    private bool isAttaching;
    private string attachmentError = string.Empty;
    public string AttachmentError { get => attachmentError; private set { attachmentError = value; Notify(); } }
    public ObservableCollection<ChatAttachment> Attachments { get; } = [];

    public async Task AddFilesAsync(string[] paths)
    {
        if (isAttaching) return;
        isAttaching = true; Notify(nameof(CanSend)); AttachmentError = string.Empty;
        try
        {
            foreach (var path in paths) AddAttachment(await AttachmentReader.ReadAsync(path, lifetime));
        }
        catch (Exception ex) { NEOLog.Error("attachment.failed", ex); AttachmentError = ex.Message; }
        finally { isAttaching = false; Notify(nameof(CanSend)); }
    }
    public void AddAttachment(ChatAttachment attachment)
    {
        if (Attachments.Count >= 6) throw new InvalidOperationException("يمكنك إرفاق ستة ملفات في الرسالة الواحدة.");
        if (Attachments.Sum(a => a.ImageBytes?.Length ?? 0) + (attachment.ImageBytes?.Length ?? 0) > 24 * 1024 * 1024)
            throw new InvalidOperationException("الحجم الإجمالي للصور يتجاوز 24 ميغابايت.");
        if (Attachments.Sum(a => a.Text?.Length ?? 0) + (attachment.Text?.Length ?? 0) > AttachmentReader.MaxCharacters)
            throw new InvalidOperationException("محتوى المرفقات النصي طويل جداً. أرفق جزءاً أقصر.");
        Attachments.Add(attachment); Notify(nameof(CanSend));
        NEOLog.Write("attachment.added", new { attachment.MediaType, bytes = attachment.ImageBytes?.Length ?? 0, characters = attachment.Text?.Length ?? 0 });
    }
    public void RemoveAttachment(ChatAttachment attachment) { Attachments.Remove(attachment); Notify(nameof(CanSend)); }
    public VoiceSessionState VoiceState => voice.State;
    public string VoiceStatus => VoiceState switch
    {
        VoiceSessionState.Connecting => "جارٍ الاتصال…",
        VoiceSessionState.Ready => "جاهز",
        VoiceSessionState.Listening => "أستمع إليك",
        VoiceSessionState.Processing => "أفكّر",
        VoiceSessionState.Speaking => "أتحدث",
        VoiceSessionState.Interrupted => "تمت المقاطعة",
        VoiceSessionState.Error => voice.LastError ?? "تعذّر تشغيل الصوت",
        _ => "الصوت متوقف"
    };
    public float InputLevel => voice.InputLevel;
    public float OutputLevel => voice.OutputLevel;
    public float VoiceLevel => Math.Max(InputLevel, OutputLevel);
    public double VoiceBar1 => 3 + VoiceLevel * 13;
    public double VoiceBar2 => 3 + VoiceLevel * 20;
    public double VoiceBar3 => 3 + VoiceLevel * 28;
    public double VoiceBar4 => 3 + VoiceLevel * 18;
    public double VoiceBar5 => 3 + VoiceLevel * 11;
    public bool IsVoiceActive => VoiceState is VoiceSessionState.Listening or VoiceSessionState.Processing or VoiceSessionState.Speaking;
    public bool CanStartVoice => VoiceState is VoiceSessionState.Disconnected or VoiceSessionState.Ready or VoiceSessionState.Interrupted or VoiceSessionState.Error;
    public ObservableCollection<ChatMessage> Conversation { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task StartVoiceAsync()
    {
        NEOLog.Write("ui.voice.start");
        await voice.StartListeningAsync(lifetime).ConfigureAwait(true);
    }
    public async Task StopVoiceAsync()
    {
        NEOLog.Write("ui.voice.stop");
        await voice.StopListeningAsync(lifetime).ConfigureAwait(true);
    }
    public void CancelVoice() => _ = voice.CancelAsync(lifetime);

    public async Task CheckAsync(CancellationToken token)
    {
        if (IsChecking) return;
        machine.TryTransition(AssistantState.Connecting);
        NotifyState();
        var result = await health.CheckAsync(token);
        Message = result.Available ? "متصل" : "غير متصل";
        serverAvailable = result.Available;
        Notify(nameof(IsServerConnected));
        Notify(nameof(ConnectionBrush)); Notify(nameof(ConnectionLabel));
        machine.TryTransition(result.Available ? AssistantState.Ready : AssistantState.Disconnected);
        NotifyState();
    }

    public async Task SendAsync()
    {
        var prompt = InputText.Trim();
        if (!CanSend) return;
        NEOLog.Write("chat.request", new { characters = prompt.Length, messages = Conversation.Count });
        InputText = string.Empty;
        Conversation.Add(new ChatMessage("user", string.IsNullOrWhiteSpace(prompt) ? "حلّل المرفقات." : prompt, Attachments.ToArray()));
        Attachments.Clear();
        IsSending = true;
        machine.TryTransition(AssistantState.Processing);
        Message = "لحظة، أفكّر في الإجابة…";
        NotifyState();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        requestCancellation = cancellation;
        try
        {
            var response = await chat.SendAsync(Conversation, cancellation.Token);
            NEOLog.Write("chat.complete", new { characters = response.Length });
            Conversation.Add(new ChatMessage("assistant", response));
            Message = "وصل الرد";
            machine.TryTransition(AssistantState.Ready);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!lifetime.IsCancellationRequested)
            {
                Conversation.Add(new ChatMessage("assistant", "توقف الرد."));
                Message = "توقف الرد.";
                machine.TryTransition(AssistantState.Interrupted);
                machine.TryTransition(AssistantState.Ready);
            }
        }
        catch (Exception ex)
        {
            NEOLog.Error("chat.failed", ex);
            Conversation.Add(new ChatMessage("assistant", $"تعذّر إكمال طلبك. حاول مجدداً."));
            Message = "تعذّر إكمال المحادثة. حاول مجدداً.";
            machine.TryTransition(AssistantState.Error);
        }
        finally
        {
            requestCancellation = null;
            IsSending = false;
            NotifyState();
        }
    }

    public void StopGeneration() => requestCancellation?.Cancel();
    private void NotifyState() { Notify(nameof(State)); Notify(nameof(Status)); Notify(nameof(IsChecking)); }
    private void OnVoiceStateChanged(VoiceSessionState state)
    {
        if (state == VoiceSessionState.Disconnected) serverAvailable = false;
        DispatchVoiceNotifications();
    }
    private void OnAudioLevelChanged(float _, float __) => DispatchVoiceNotifications();
    private void OnVoiceTextDelta(string delta) => Dispatch(() => LiveResponse += delta);
    private void OnVoiceResponseCompleted(VoiceResponse response)
    {
        Dispatch(() => LiveResponse = string.Empty);
        if (!response.Cancelled && !string.IsNullOrWhiteSpace(response.Text))
            Dispatch(() => Conversation.Add(new ChatMessage("assistant", response.Text.Trim())));
    }
    private void DispatchVoiceNotifications() => Dispatch(() =>
    {
        Notify(nameof(IsServerConnected));
        Notify(nameof(IsSpeaking)); Notify(nameof(IsProcessing)); Notify(nameof(ConnectionBrush)); Notify(nameof(ConnectionLabel)); Notify(nameof(VoiceState)); Notify(nameof(VoiceStatus)); Notify(nameof(InputLevel)); Notify(nameof(OutputLevel)); Notify(nameof(VoiceLevel)); Notify(nameof(MicrophoneGlyph));
        Notify(nameof(VoiceBar1)); Notify(nameof(VoiceBar2)); Notify(nameof(VoiceBar3)); Notify(nameof(VoiceBar4)); Notify(nameof(VoiceBar5)); Notify(nameof(IsVoiceActive)); Notify(nameof(CanStartVoice)); Notify(nameof(Status));
    });
    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else _ = dispatcher.InvokeAsync(action);
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        voice.StateChanged -= OnVoiceStateChanged;
        voice.AudioLevelChanged -= OnAudioLevelChanged;
        voice.TextDelta -= OnVoiceTextDelta;
        voice.ResponseCompleted -= OnVoiceResponseCompleted;
        requestCancellation?.Cancel();
    }
}
