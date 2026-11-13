using System.Net.WebSockets;
using System.Text;

namespace NEO.Infrastructure;

internal sealed class RealtimeConnection(Uri endpoint, Action<string, object?> log) : IAsyncDisposable
{
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private ClientWebSocket? socket;
    private RealtimeProtocol? protocol;

    public WebSocketState State => socket?.State ?? WebSocketState.None;
    public bool IsOpen => State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (socket != null) throw new InvalidOperationException("Realtime connection has already been initialized.");
        var client = new ClientWebSocket { Options = { KeepAliveInterval = TimeSpan.FromSeconds(15) } };
        try
        {
            await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            socket = client;
            protocol = new RealtimeProtocol(client, sendGate, log);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task SendSessionUpdateAsync(int inputRate, int outputRate, CancellationToken ct) => GetProtocol().SendSessionUpdateAsync(inputRate, outputRate, ct);
    public Task AppendAudioAsync(string audio, CancellationToken ct) => GetProtocol().AppendAudioAsync(audio, ct);
    public Task SendControlAsync(string type, CancellationToken ct) => GetProtocol().SendControlAsync(type, ct);
    public Task CreateResponseAsync(CancellationToken ct) => GetProtocol().CreateResponseAsync(ct);

    public async Task<string?> ReceiveMessageAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var client = socket ?? throw new InvalidOperationException("Realtime connection is not connected.");
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                log("ws.closed", new { result.CloseStatus, result.CloseStatusDescription });
                return null;
            }
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
    }

    public async Task CloseAsync()
    {
        var client = Interlocked.Exchange(ref socket, null);
        protocol = null;
        if (client == null) return;
        if (client.State == WebSocketState.Open)
        {
            try { await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "NEO closing", CancellationToken.None).ConfigureAwait(false); }
            catch (WebSocketException) { }
        }
        client.Dispose();
    }

    private RealtimeProtocol GetProtocol() => protocol ?? throw new InvalidOperationException("Realtime connection is not connected.");

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        sendGate.Dispose();
    }
}
