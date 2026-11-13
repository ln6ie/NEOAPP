using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace NEO.Infrastructure;

internal sealed class AudioPlayback(Action<Exception>? onError = null) : IDisposable
{
    private readonly object gate = new();
    private PcmPlaybackBuffer? buffer;
    private WasapiOut? output;
    private CancellationTokenSource cancel = new();
    private int sampleRate;
    private bool disposed;
    public long ConsumedBytes { get { lock (gate) return buffer?.ConsumedBytes ?? 0; } }
    public int BufferedBytes { get { lock (gate) return buffer?.BufferedBytes ?? 0; } }
    public event Action<float>? LevelChanged;

    public async Task EnqueueAsync(byte[] bytes, int rate, CancellationToken ct)
    {
        if (bytes.Length == 0) return;
        EnsureInitialized(rate);
        PcmPlaybackBuffer target;
        CancellationToken playbackToken;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            target = buffer!;
            playbackToken = cancel.Token;
            if (output?.PlaybackState != PlaybackState.Playing) output?.Play();
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, playbackToken);
        try { await target.EnqueueAsync(bytes, linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (playbackToken.IsCancellationRequested) { return; }
        LevelChanged?.Invoke(PcmAudio.Level(bytes));
    }

    public void Stop()
    {
        lock (gate)
        {
            cancel.Cancel(); cancel.Dispose(); cancel = new CancellationTokenSource();
            buffer?.ClearBuffer();
            output?.Stop();
        }
        LevelChanged?.Invoke(0);
    }

    private void EnsureInitialized(int rate)
    {
        lock (gate)
        {
            if (buffer != null && sampleRate == rate) return;
            output?.Stop(); output?.Dispose();
            buffer = new PcmPlaybackBuffer(rate);
            output = new WasapiOut(AudioClientShareMode.Shared, 50);
            output.Init(buffer);
            output.PlaybackStopped += (_, e) => { if (e.Exception != null) onError?.Invoke(e.Exception); };
            sampleRate = rate;
            NEOLog.Write("speaker.initialized", new { outputSampleRate = rate, latencyMilliseconds = 50, bufferSeconds = 2 });
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            cancel.Cancel(); cancel.Dispose();
            output?.Stop(); output?.Dispose(); output = null; buffer = null;
        }
    }
}
