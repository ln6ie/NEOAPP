using System.Net.WebSockets;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NEO.Core;
using NEO.Infrastructure;

internal static class ArabicMicrophoneCheck
{
    // Explicit user-invoked diagnostic; audio stays in RAM and is never written to disk.
    public static async Task RunAsync()
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEO", "NEO.settings.json");
        var settings = await new SettingsStore(settingsPath).LoadAsync(default);
        using var devices = new MMDeviceEnumerator();
        using var device = string.IsNullOrEmpty(settings.MicrophoneDeviceId) ? devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : devices.GetDevice(settings.MicrophoneDeviceId);
        using var capture = new WasapiCapture(device) { WaveFormat = new WaveFormat(16000, 16, 1) };
        using var samples = new MemoryStream();
        var sync = new object();
        var peak = 0d;
        capture.DataAvailable += (_, e) =>
        {
            lock (sync)
            {
                samples.Write(e.Buffer, 0, e.BytesRecorded);
                for (var i = 0; i + 1 < e.BytesRecorded; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768d));
            }
        };
        Console.WriteLine($"MICROPHONE={device.FriendlyName} MUTED={device.AudioEndpointVolume.Mute}");
        Console.WriteLine("تحدث الآن بالعربية لمدة ثماني ثوانٍ. سيُعرض النص الذي تعرّف عليه النموذج هنا فقط.");
        capture.StartRecording();
        await Task.Delay(8000);
        capture.StopRecording();
        byte[] pcm;
        lock (sync) pcm = samples.ToArray();
        Console.WriteLine($"PCM_BYTES={pcm.Length} RAW_PEAK={peak:F6}");
        if (peak < 0.0003) throw new InvalidOperationException("الإشارة صامتة أو منخفضة جداً. اختر الميكروفون الصحيح وأعد الاختبار أثناء الكلام.");
        var gain = new PcmInputGain();
        for (var i = 0; i < pcm.Length; i += 1600) gain.Apply(pcm.AsSpan(i, Math.Min(1600, pcm.Length - i)));
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await socket.ConnectAsync(new Uri(settings.RealtimeEndpoint), timeout.Token);
        using var created = await SpeakerCheck.ReceiveAsync(socket, timeout.Token);
        await SpeakerCheck.SendAsync(socket, new
        {
            type = "session.update",
            session = new { type = "transcription", audio = new { input = new { format = new { type = "audio/pcm", rate = 16000 }, transcription = new { language = "ar" } } } }
        }, timeout.Token);
        await SpeakerCheck.SendAsync(socket, new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(pcm) }, timeout.Token);
        await SpeakerCheck.SendAsync(socket, new { type = "input_audio_buffer.commit" }, timeout.Token);
        while (true)
        {
            using var message = await SpeakerCheck.ReceiveAsync(socket, timeout.Token);
            var root = message.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "error") throw new InvalidOperationException(root.ToString());
            if (type == "conversation.item.input_audio_transcription.completed")
            {
                Console.WriteLine($"ARABIC_TRANSCRIPT={root.GetProperty("transcript").GetString()}");
                break;
            }
        }
    }
}
