using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NEO.Core;
using NEO.Infrastructure;

namespace NEO.Tests;

public sealed class ServerManagerTests
{
    private static readonly NEOSettings Settings = new();
    private static string Props(string? path = null) => JsonSerializer.Serialize(new { model_path = path ?? Settings.ServerModelPath, modalities = new { audio = true }, build_info = "b12360-2d8a32502" });
    private static LlamaServerManager Manager(HttpClient http) => new(http, "not-a-launchable-host.exe", Path.Combine(Path.GetTempPath(), "NEO-server-tests"));

    [Fact]
    public async Task ReusesCompatibleServerAndNeverOwnsOrStopsIt()
    {
        using var http = new HttpClient(new Handler((r, _) => Task.FromResult(Ok(r.RequestUri!.AbsolutePath == "/health" ? "{\"status\":\"ok\"}" : Props()))));
        await using var manager = Manager(http);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => manager.EnsureReadyAsync(Settings, default)));
        Assert.Equal(LlamaServerState.Ready, manager.State);
        Assert.False(manager.OwnsServer);
        await manager.StopAsync();
        Assert.Equal(LlamaServerState.Disconnected, manager.State);
        Assert.Equal(LlamaServerState.Ready, await Recheck());
        async Task<LlamaServerState> Recheck() { await manager.EnsureReadyAsync(Settings, default); return manager.State; }
    }
    [Fact]
    public async Task AnotherModelIsRejectedWithoutLaunchingOrOwningAnything()
    {
        using var http = new HttpClient(new Handler((r, _) => Task.FromResult(Ok(r.RequestUri!.AbsolutePath == "/health" ? "{\"status\":\"ok\"}" : Props("C:/other/model.gguf")))));
        await using var manager = Manager(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.EnsureReadyAsync(Settings, default));
        Assert.Equal(LlamaServerState.Error, manager.State);
        Assert.False(manager.OwnsServer);
    }
    [Fact]
    public async Task DisabledManagementDoesNotStartMissingServer()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused))));
        await using var manager = Manager(http);
        await manager.EnsureReadyAsync(Settings with { AutoManageServer = false }, default);
        Assert.Equal(LlamaServerState.Disconnected, manager.State);
        Assert.False(manager.OwnsServer);
    }
    [Fact]
    public async Task CancellationDuringLoadingDoesNotTakeOwnershipOfExternalServer()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        await using var manager = Manager(http);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.EnsureReadyAsync(Settings, cancel.Token));
        Assert.False(manager.OwnsServer);
        Assert.Equal(LlamaServerState.Disconnected, manager.State);
    }
    [Fact]
    public void ArgumentsPreservePathsAndRequireLocalCudaOffline()
    {
        var settings = Settings with { ServerModelPath = "C:/models/a ; b $(test).gguf" };
        var args = LlamaServerManager.ServerArguments(settings, 8081);
        Assert.Equal(settings.ServerModelPath, args[1]);
        Assert.Contains("CUDA0", args);
        Assert.Contains("--offline", args);
        Assert.Contains("--tts-lang", args);
        Assert.Contains("en", args);
        Assert.Contains("--tts-voice", args);
        Assert.Contains("female", args);
        Assert.Equal("127.0.0.1", args[Array.IndexOf(args, "--host") + 1]);
        Assert.DoesNotContain("0.0.0.0", args);
    }
    [Theory]
    [InlineData("{\"model_path\":\"C:/model.gguf\",\"modalities\":{\"audio\":true},\"build_info\":\"b1\"}")]
    [InlineData("{\"model_path\":\"C:/model.gguf\",\"modalities\":{\"audio\":false},\"build_info\":\"b12360\"}")]
    public void RejectsUnverifiedBuildOrMissingAudio(string json)
    {
        using var props = JsonDocument.Parse(json);
        Assert.Throws<InvalidOperationException>(() => LlamaServerManager.VerifyIdentity(props.RootElement, "C:/model.gguf"));
    }
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => handle(r, t); }
}
