using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NEO.Core;
using NAudio.CoreAudioApi;
using Forms = System.Windows.Forms;

namespace NEO.Views;

public partial class SettingsView : UserControl
{
    public event Action? Completed;
    private readonly Settings original;
    private readonly Func<Settings, Task> save;
    public SettingsView(Settings settings, Func<Settings, Task> saveSettings)
    {
        InitializeComponent(); original = settings; save = saveSettings;
        Http.Text = settings.HttpEndpoint; Realtime.Text = settings.RealtimeEndpoint;
        foreach (var screen in Forms.Screen.AllScreens) Monitor.Items.Add(screen.DeviceName);
        Monitor.SelectedIndex = Math.Min(settings.Monitor, Monitor.Items.Count - 1);
        TopmostOption.IsChecked = settings.AlwaysOnTop; Fullscreen.IsChecked = settings.HideInFullscreen;
        AutoVoice.IsChecked = settings.AutoStartVoice;
        AutoServer.IsChecked = settings.AutoManageServer;
        Microphone.Items.Add(new MicrophoneOption(null, "ميكروفون المحادثات الافتراضي في ويندوز"));
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device) Microphone.Items.Add(new MicrophoneOption(device.ID, device.FriendlyName));
            }
        }
        catch (Exception ex) { NEO.Infrastructure.NEOLog.Error("mic.enumerate.failed", ex); }
        Microphone.SelectedIndex = 0;
        foreach (MicrophoneOption option in Microphone.Items)
            if (option.Id == settings.MicrophoneDeviceId) Microphone.SelectedItem = option;
    }
    private async void Save(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            var settings = original with { HttpEndpoint = Http.Text.Trim(), RealtimeEndpoint = Realtime.Text.Trim(), Monitor = Monitor.SelectedIndex, AlwaysOnTop = TopmostOption.IsChecked == true, HideInFullscreen = Fullscreen.IsChecked == true };
            settings = settings with { AutoStartVoice = AutoVoice.IsChecked == true, AutoManageServer = AutoServer.IsChecked == true, MicrophoneDeviceId = (Microphone.SelectedItem as MicrophoneOption)?.Id };
            settings.Validate(); await save(settings); Completed?.Invoke();
        }
        catch (Exception ex) { NEO.Infrastructure.NEOLog.Error("settings.save.failed", ex); Error.Text = "تعذّر حفظ الإعدادات. تحقّق من عناوين الاتصال وحاول مجدداً."; }
        finally { SaveButton.IsEnabled = true; }
    }
    private sealed record MicrophoneOption(string? Id, string Name);
}

