using System.Net.WebSockets;
using System.Text.Json;
using NEO.Core;

namespace NEO.Infrastructure;

internal sealed class RealtimeProtocol(ClientWebSocket socket, SemaphoreSlim sendGate, Action<string, object?> log)
{
    public Task SendSessionUpdateAsync(int inputRate, int outputRate, CancellationToken ct) => SendAsync(new { type = "session.update", session = new { type = "realtime", instructions = Language.VoiceInstructions, output_modalities = new[] { "text", "audio" }, audio = new { input = new { format = new { type = "audio/pcm", rate = inputRate } }, output = new { format = new { type = "audio/pcm", rate = outputRate }, voice = "female" } }, max_output_tokens = 1024 } }, ct);
    public Task AppendAudioAsync(string audio, CancellationToken ct) => SendAsync(new { type = "input_audio_buffer.append", audio }, ct, false);
    public Task SendControlAsync(string type, CancellationToken ct) => SendAsync(new { type }, ct);
    public Task CreateResponseAsync(CancellationToken ct) => SendAsync(new { type = "response.create", response = new { output_modalities = new[] { "text", "audio" } } }, ct);

    private async Task SendAsync(object value, CancellationToken ct, bool logEvent = true)
    {
        if (socket.State != WebSocketState.Open) throw new WebSocketException("Realtime websocket is closed.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (logEvent)
        {
            using var json = JsonDocument.Parse(bytes);
            log("ws.send", new { type = json.RootElement.GetProperty("type").GetString(), bytes = bytes.Length });
        }
        await sendGate.WaitAsync(ct).ConfigureAwait(false);
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
        finally { sendGate.Release(); }
    }
}
