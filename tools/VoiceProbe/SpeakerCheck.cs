using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NEO.Infrastructure;
using NEO.Core;
using System.Text;

internal static class SpeakerCheck
{
    public static async Task RunAsync(bool arabic = false, string? question = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:8081/v1/realtime"), timeout.Token);
        using var session = await ReceiveAsync(socket, timeout.Token);
        var rate = session.RootElement.GetProperty("session").GetProperty("audio").GetProperty("output").GetProperty("format").GetProperty("rate").GetInt32();
        var queue = new PcmPlaybackBuffer(rate);
        using var speaker = new WasapiOut(AudioClientShareMode.Shared, 50);
        speaker.Init(queue);
        await SendAsync(socket, new { type = "session.update", session = new { instructions = arabic ? Language.Instructions : "Speak clearly and naturally.", output_modalities = new[] { "text", "audio" }, audio = new { output = new { voice = "female", language = arabic ? Language.SpeechCode : "en" } } } }, timeout.Token);
        var prompt = arabic ? "قل بالعربية فقط: أهلاً، أنا نيو، مساعدك الشخصي. كيف يمكنني مساعدتك اليوم؟" : "Say exactly: Hello, I am NEO, your local voice assistant. I will speak clearly and naturally, and listen when you finish talking.";
        if (question != null) prompt = question;
        await SendAsync(socket, new { type = "conversation.item.create", item = new { type = "message", role = "user", content = new[] { new { type = "input_text", text = prompt } } } }, timeout.Token);
        await SendAsync(socket, new { type = "response.create", response = new { output_modalities = new[] { "text", "audio" }, max_output_tokens = 256 } }, timeout.Token);
        long received = 0;
        var chunks = 0;
        var elapsed = new Stopwatch();
        var transcript = new StringBuilder();
        while (true)
        {
            using var message = await ReceiveAsync(socket, timeout.Token);
            var root = message.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "error") throw new InvalidOperationException(root.ToString());
            if (type == "response.output_text.delta") transcript.Append(root.GetProperty("delta").GetString());
            if (type == "response.output_audio.delta")
            {
                var bytes = Convert.FromBase64String(root.GetProperty("delta").GetString()!);
                if (!elapsed.IsRunning) { elapsed.Start(); speaker.Play(); }
                received += bytes.Length;
                chunks++;
                await queue.EnqueueAsync(bytes, timeout.Token);
            }
            if (type == "response.done") break;
        }
        while (queue.BufferedBytes > 0) await Task.Delay(10, timeout.Token);
        await Task.Delay(100, timeout.Token);
        speaker.Stop();
        Console.WriteLine($"SAMPLE_RATE={rate} CHUNKS={chunks} RECEIVED_BYTES={received} CONSUMED_BYTES={queue.ConsumedBytes} PCM_DURATION_SECONDS={received / (rate * 2d):F3} PLAYBACK_WALL_SECONDS={elapsed.Elapsed.TotalSeconds:F3}");
        Console.WriteLine($"LANGUAGE={(arabic ? "ar" : "en")} TEXT={transcript}");
        if (received == 0 || received != queue.ConsumedBytes) throw new InvalidOperationException("Generated PCM was lost before reaching WASAPI.");
        if (arabic && !transcript.ToString().Any(c => c is >= '\u0600' and <= '\u06FF')) throw new InvalidOperationException("The model did not return Arabic text.");
    }

    internal static Task SendAsync(ClientWebSocket socket, object data, CancellationToken token) =>
        socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(data)), WebSocketMessageType.Text, true, token);

    internal static async Task<JsonDocument> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        var bytes = new byte[65536];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), token);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Server closed websocket.");
            stream.Write(bytes, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(stream.ToArray());
    }
}
