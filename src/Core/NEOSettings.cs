namespace NEO.Core;

public sealed record NEOSettings
{
    public string HttpEndpoint { get; init; } = "http://127.0.0.1:8081/";
    public string RealtimeEndpoint { get; init; } = "ws://127.0.0.1:8081/v1/realtime";
    public double Width { get; init; } = 248;
    public double Height { get; init; } = 48;
    public double TopOffset { get; init; } = 10;
    public double Opacity { get; init; } = 0.98;
    public int Monitor { get; init; }
    public bool AlwaysOnTop { get; init; } = true;
    public bool HideInFullscreen { get; init; } = true;
    public bool AutoStartVoice { get; init; } = true;
    public string? MicrophoneDeviceId { get; init; }
    public bool AutoManageServer { get; init; } = true;
    public string ServerExecutablePath { get; init; } = DefaultServerExecutable();
    public string ServerModelPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "models", "LiquidAI", "LFM3-Omni-2B-GGUF", "LFM3-Omni-2B-F16.gguf");
    public string ServerProjectorPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "models", "LiquidAI", "LFM3-Omni-2B-GGUF", "mmproj-LFM3-Omni-2B-F16.gguf");
    private static string DefaultServerExecutable()
    {
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "llama.cpp", "llama-server.exe");
        var machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "llama.cpp", "llama-server.exe");
        return File.Exists(user) || !File.Exists(machine) ? user : machine;
    }

    public void Validate()
    {
        ValidateEndpoint(HttpEndpoint, "http", "https");
        ValidateEndpoint(RealtimeEndpoint, "ws", "wss");
        if (!double.IsFinite(Width) || Width is < 200 or > 600
            || !double.IsFinite(Height) || Height is < 40 or > 100
            || !double.IsFinite(TopOffset) || TopOffset is < 0 or > 200
            || !double.IsFinite(Opacity) || Opacity is < 0.4 or > 1 || Monitor < 0)
            throw new ArgumentException("NEO window settings are outside the supported range.");
    }

    private static void ValidateEndpoint(string value, string plain, string secure)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != plain && uri.Scheme != secure) || !uri.IsLoopback
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("NEO currently accepts loopback endpoints only, without embedded credentials.");
    }
}

public sealed record ConnectionStatus(bool Available, string Message);
public interface IHealthClient
{
    Task<ConnectionStatus> CheckAsync(CancellationToken cancellationToken);
}
