using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using NEO.Core;

namespace NEO.Infrastructure;

public enum LlamaServerState { Disconnected, Loading, Ready, Error }

public interface ILlamaServerManager : IAsyncDisposable
{
    LlamaServerState State { get; }
    bool OwnsServer { get; }
    event Action<LlamaServerState>? StateChanged;
    Task EnsureReadyAsync(NEOSettings settings, CancellationToken token);
    Task StopAsync(CancellationToken token = default);
}

/// <summary>Injected HTTP transport; serialized, loopback-only lifecycle with explicit ownership.</summary>
public sealed class LlamaServerManager(HttpClient http, string hostExecutable, string lockDirectory) : ILlamaServerManager
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? host;
    private Task? stdout, stderr;
    private bool disposed;
    private string? ownedConfiguration;
    public LlamaServerState State { get; private set; }
    public bool OwnsServer => host is { HasExited: false };
    public event Action<LlamaServerState>? StateChanged;

    public async Task EnsureReadyAsync(NEOSettings settings, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            settings.Validate();
            var endpoint = new Uri(settings.HttpEndpoint);
            if (!settings.AutoManageServer)
            {
                await StopOwnedAsync().ConfigureAwait(false);
                SetState(await ProbeAsync(endpoint, settings.ServerModelPath, token) == Probe.Ready ? LlamaServerState.Ready : LlamaServerState.Disconnected);
                return;
            }
            if (endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" || endpoint.AbsolutePath != "/")
                throw new InvalidOperationException("Automatic management requires an http://127.0.0.1:port/ endpoint.");
            var realtime = new Uri(settings.RealtimeEndpoint);
            if (realtime.Scheme != "ws" || realtime.Host != endpoint.Host || realtime.Port != endpoint.Port || realtime.AbsolutePath != "/v1/realtime")
                throw new InvalidOperationException("Realtime endpoint must use the same managed loopback server.");
            var configuration = JsonSerializer.Serialize(new { settings.ServerExecutablePath, settings.ServerModelPath, settings.ServerProjectorPath, settings.HttpEndpoint });
            if (host != null && ownedConfiguration != configuration) await StopOwnedAsync().ConfigureAwait(false);
            Directory.CreateDirectory(lockDirectory);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            await using var processLock = await AcquireLockAsync(Path.Combine(lockDirectory, $"server-{endpoint.Port}.lock"), deadline.Token).ConfigureAwait(false);
            var probe = await ProbeAsync(endpoint, settings.ServerModelPath, deadline.Token).ConfigureAwait(false);
            if (probe == Probe.Ready) { SetState(LlamaServerState.Ready); NEOLog.Write("server.reused", new { endpoint.Port, owned = OwnsServer }); return; }
            SetState(LlamaServerState.Loading);
            if (probe == Probe.Absent && !OwnsServer)
            {
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == endpoint.Port))
                    throw new InvalidOperationException("The server port is occupied by an unverified process; it was left untouched.");
                ValidateFiles(settings);
                var start = new ProcessStartInfo(hostExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(settings.ServerExecutablePath)! };
                start.ArgumentList.Add(settings.ServerExecutablePath);
                foreach (var value in ServerArguments(settings, endpoint.Port)) start.ArgumentList.Add(value);
                host?.Dispose();
                host = Process.Start(start) ?? throw new InvalidOperationException("Server host failed to start.");
                ownedConfiguration = configuration;
                stdout = ReadLogAsync(host.StandardOutput, "stdout"); stderr = ReadLogAsync(host.StandardError, "stderr");
                NEOLog.Write("server.started", new { hostPid = host.Id, endpoint.Port, settings.ServerExecutablePath });
            }
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (host is { HasExited: true }) throw new InvalidOperationException($"Owned server exited ({host.ExitCode}); inspect server.output diagnostics.");
                if (await ProbeAsync(endpoint, settings.ServerModelPath, deadline.Token).ConfigureAwait(false) == Probe.Ready) break;
                await Task.Delay(300, deadline.Token).ConfigureAwait(false);
            }
            SetState(LlamaServerState.Ready);
        }
        catch (OperationCanceledException) { await StopOwnedAsync().ConfigureAwait(false); SetState(LlamaServerState.Disconnected); throw; }
        catch { await StopOwnedAsync().ConfigureAwait(false); SetState(LlamaServerState.Error); throw; }
        finally { gate.Release(); }
    }

    public static string[] ServerArguments(NEOSettings settings, int port) => ["-m", settings.ServerModelPath, "--mmproj", settings.ServerProjectorPath, "-ngl", "99", "-c", "4096", "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--device", "CUDA0", "--offline", "--reasoning", "off", "--tts-lang", "en", "--tts-voice", "female"];

    private static void ValidateFiles(NEOSettings settings)
    {
        foreach (var path in new[] { settings.ServerExecutablePath, settings.ServerModelPath, settings.ServerProjectorPath })
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new FileNotFoundException("A configured server/model/projector file is missing.", path);
        foreach (var dll in new[] { "llama-server-impl.dll", "llama.dll", "ggml.dll", "ggml-base.dll", "ggml-cuda.dll", "mtmd.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cudart64_12.dll" })
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(settings.ServerExecutablePath)!, dll))) throw new FileNotFoundException("Installed CUDA server dependency missing.", dll);
    }

    private enum Probe { Absent, Loading, Ready }
    private async Task<Probe> ProbeAsync(Uri endpoint, string modelPath, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await http.GetAsync(new Uri(endpoint, "health"), timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable) return Probe.Loading;
            response.EnsureSuccessStatusCode();
            using var health = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            if (!health.RootElement.TryGetProperty("status", out var status) || status.GetString() != "ok") throw new InvalidOperationException("Unrecognized server health response.");
            using var propsResponse = await http.GetAsync(new Uri(endpoint, "props"), timeout.Token).ConfigureAwait(false);
            propsResponse.EnsureSuccessStatusCode();
            using var props = JsonDocument.Parse(await propsResponse.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            VerifyIdentity(props.RootElement, modelPath);
            return Probe.Ready;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused }) { return Probe.Absent; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Probe.Loading; }
    }

    public static void VerifyIdentity(JsonElement props, string modelPath)
    {
        if (!props.TryGetProperty("build_info", out var build) || !(build.GetString()?.Contains("b12360", StringComparison.Ordinal) ?? false))
            throw new InvalidOperationException("The running server is not the verified b12360 build.");
        if (!props.TryGetProperty("model_path", out var path) || !string.Equals(Path.GetFullPath(path.GetString() ?? ""), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The existing server uses another or unverifiable model; it was left untouched.");
        if (!props.TryGetProperty("modalities", out var modalities) || !modalities.TryGetProperty("audio", out var audio) || audio.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("The existing server has no verified audio projector capability.");
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    private static async Task ReadLogAsync(StreamReader reader, string stream)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            // Do not retain prompts, token text or raw server HTTP request bodies.
            if (line.Contains("CUDA", StringComparison.OrdinalIgnoreCase) || line.Contains("offload", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.StartsWith("NEO_SERVER_", StringComparison.Ordinal) || line.Contains("listening", StringComparison.OrdinalIgnoreCase) || line.Contains("signal", StringComparison.OrdinalIgnoreCase))
                NEOLog.Write("server.output", new { stream, line = line[..Math.Min(line.Length, 2000)] });
    }
    private void SetState(LlamaServerState state) { State = state; NEOLog.Write("server.state", new { state = state.ToString() }); StateChanged?.Invoke(state); }
    private async Task StopOwnedAsync()
    {
        if (host == null) return;
        try
        {
            if (!host.HasExited)
            {
                await host.StandardInput.WriteLineAsync("stop").ConfigureAwait(false); host.StandardInput.Close();
                await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            }
            if (stdout != null && stderr != null) await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            NEOLog.Write("server.stopped", new { hostPid = host.Id, exitCode = host.ExitCode });
        }
        finally { host.Dispose(); host = null; }
    }
    public async Task StopAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await StopOwnedAsync().ConfigureAwait(false); SetState(LlamaServerState.Disconnected); }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); disposed = true; }
}
