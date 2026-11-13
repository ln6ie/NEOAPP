namespace NEO.Core;

public enum VoiceSessionState
{
    Disconnected,
    Connecting,
    Ready,
    Listening,
    Processing,
    Speaking,
    Interrupted,
    Error
}

public sealed record VoiceResponse(string Text, bool Cancelled);

public interface IVoiceSession : IAsyncDisposable
{
    VoiceSessionState State { get; }
    float InputLevel { get; }
    float OutputLevel { get; }
    float PeakInputLevel { get; }
    float PeakOutputLevel { get; }
    string? LastError { get; }
    long CapturedBytes { get; }
    long SentBytes { get; }
    long ReceivedAudioBytes { get; }

    event Action<VoiceSessionState>? StateChanged;
    event Action<float, float>? AudioLevelChanged;
    event Action<string>? TextDelta;
    event Action<VoiceResponse>? ResponseCompleted;

    Task StartListeningAsync(CancellationToken cancellationToken = default);
    Task StopListeningAsync(CancellationToken cancellationToken = default);
    Task CancelAsync(CancellationToken cancellationToken = default);
}
