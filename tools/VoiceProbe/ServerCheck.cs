using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NEO.Core;
using NEO.Infrastructure;

internal static class ServerCheck
{
    public static async Task RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var http = new HttpClient();
        var endpoint = new Uri("http://127.0.0.1:8081/");
        using var health = await http.GetAsync(new Uri(endpoint, "health"), deadline.Token);
        health.EnsureSuccessStatusCode();
        Console.WriteLine("HEALTH " + await health.Content.ReadAsStringAsync(deadline.Token));
        using var props = await http.GetFromJsonAsync<JsonDocument>(new Uri(endpoint, "props"), deadline.Token) ?? throw new InvalidOperationException("No props");
        LlamaServerManager.VerifyIdentity(props.RootElement, new NEOSettings().ServerModelPath);
        Console.WriteLine("MODEL " + props.RootElement.GetProperty("model_path").GetString());
        Console.WriteLine("BUILD " + props.RootElement.GetProperty("build_info").GetString());
        var clock = Stopwatch.StartNew();
        using var chat = await http.PostAsJsonAsync(new Uri(endpoint, "v1/chat/completions"), new
        {
            messages = new[] { new { role = "system", content = "Reply with text only." }, new { role = "user", content = "What is 2 + 2? Answer in one short sentence." } },
            max_tokens = 64, temperature = 0, stream = false
        }, deadline.Token);
        chat.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await chat.Content.ReadAsStringAsync(deadline.Token));
        var text = result.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("No real chat response.");
        Console.WriteLine($"CHAT_MS {clock.ElapsedMilliseconds}");
        Console.WriteLine("CHAT_RESPONSE " + text);
        using var socket = new ClientWebSocket();
        clock.Restart();
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:8081/v1/realtime"), deadline.Token);
        var bytes = new byte[64 * 1024];
        var received = await socket.ReceiveAsync(bytes.AsMemory(), deadline.Token);
        using var session = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 0, received.Count));
        if (session.RootElement.GetProperty("type").GetString() != "session.created") throw new InvalidOperationException("Missing realtime session.created");
        Console.WriteLine($"REALTIME_SESSION_CREATED_MS {clock.ElapsedMilliseconds}");
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "verification complete", deadline.Token);
        await using var reused = new LlamaServerManager(http, "must-not-be-launched.exe", Path.Combine(Path.GetTempPath(), "NEO-live-check"));
        await reused.EnsureReadyAsync(new NEOSettings(), deadline.Token);
        if (reused.OwnsServer) throw new InvalidOperationException("Reuse took ownership unexpectedly.");
        await reused.StopAsync(deadline.Token);
        using var stillAlive = await http.GetAsync(new Uri(endpoint, "health"), deadline.Token);
        stillAlive.EnsureSuccessStatusCode();
        Console.WriteLine("REUSE_WITHOUT_OWNERSHIP_AND_EXTERNAL_PRESERVED true");
        Console.WriteLine("VOICE_CONVERSATION_VERIFIED false (handshake test only)");
    }
}
