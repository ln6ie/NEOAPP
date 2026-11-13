using System.Buffers;
using NEO.Core;

namespace NEO.Infrastructure;

internal sealed class AudioInput
{
    private const int SampleRate = 16000;
    private const int FrameBytesPerSecond = SampleRate * 2;
    private readonly Func<bool> isListening;
    private readonly Func<string, CancellationToken, Task> appendAudio;
    private readonly Func<CancellationToken, Task> commitTurn;
    private readonly Action<float> onLevel;
    private readonly Action<Exception> onError;
    private readonly VoiceTurnDetector turnDetector = new(1000);
    private readonly PcmInputGain gain = new();
    private readonly Queue<AudioChunk> preRoll = new();
    private readonly Microphone microphone;
    private int preRollBytes;
    private long sentBytes;
    private long lastLog;

    public long CapturedBytes => microphone.CapturedBytes;
    public long SentBytes => Interlocked.Read(ref sentBytes);
    public float PeakLevel => microphone.PeakLevel;

    public AudioInput(string? deviceId, Func<bool> isListening, Func<string, CancellationToken, Task> appendAudio,
        Func<CancellationToken, Task> commitTurn, Action<float> onLevel, Action<Exception> onError)
    {
        this.isListening = isListening;
        this.appendAudio = appendAudio;
        this.commitTurn = commitTurn;
        this.onLevel = onLevel;
        this.onError = onError;
        microphone = new Microphone(deviceId, ProcessFrameAsync, onError);
        microphone.LevelChanged += HandleLevel;
    }

    public void Start()
    {
        turnDetector.Reset();
        microphone.Start();
    }

    public async Task StopAsync()
    {
        await microphone.StopAsync().ConfigureAwait(false);
        ClearPreRoll();
    }

    private void HandleLevel(float level)
    {
        onLevel(level);
    }

    private async Task ProcessFrameAsync(AudioChunk frame)
    {
        try
        {
            if (!isListening()) return;
            var level = PcmAudio.Level(frame.Buffer.AsSpan(0, frame.Count));
            var now = Environment.TickCount64;
            if (now - lastLog >= 1000)
            {
                lastLog = now;
                NEOLog.Write("mic.level", new { level, peakInputLevel = PeakLevel, CapturedBytes, SentBytes, frameBytes = frame.Count,
                    vadThreshold = turnDetector.Threshold, speechActive = turnDetector.IsSpeechActive });
            }

            var wasActive = turnDetector.IsSpeechActive;
            var endTurn = turnDetector.Observe(level, frame.Count * 1000d / FrameBytesPerSecond);
            if (!wasActive && turnDetector.IsSpeechActive)
            {
                gain.Reset();
                NEOLog.Write("voice.speech.started", new { level, threshold = turnDetector.Threshold, preRollBytes });
            }
            if (!turnDetector.IsSpeechActive)
            {
                SavePreRoll(frame);
                return;
            }

            while (preRoll.TryDequeue(out var prior))
            {
                try { await SendFrameAsync(prior, CancellationToken.None).ConfigureAwait(false); }
                finally { ArrayPool<byte>.Shared.Return(prior.Buffer); }
            }
            preRollBytes = 0;
            await SendFrameAsync(frame, CancellationToken.None).ConfigureAwait(false);
            if (endTurn)
            {
                NEOLog.Write("voice.turn.commit", new { SentBytes, level, gain = gain.Gain, language = Language.SpeechCode });
                await commitTurn(CancellationToken.None).ConfigureAwait(false);
                turnDetector.Reset();
            }
        }
        catch (Exception ex) { onError(ex); }
        finally { ArrayPool<byte>.Shared.Return(frame.Buffer); }
    }

    private async Task SendFrameAsync(AudioChunk frame, CancellationToken cancellationToken)
    {
        gain.Apply(frame.Buffer.AsSpan(0, frame.Count));
        await appendAudio(Convert.ToBase64String(frame.Buffer, 0, frame.Count), cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref sentBytes, frame.Count);
    }

    private void SavePreRoll(AudioChunk frame)
    {
        var saved = ArrayPool<byte>.Shared.Rent(frame.Count);
        frame.Buffer.AsSpan(0, frame.Count).CopyTo(saved);
        preRoll.Enqueue(new AudioChunk(saved, frame.Count));
        preRollBytes += frame.Count;
        while (preRollBytes > FrameBytesPerSecond * 0.35 && preRoll.Count > 1)
        {
            var expired = preRoll.Dequeue();
            preRollBytes -= expired.Count;
            ArrayPool<byte>.Shared.Return(expired.Buffer);
        }
    }

    private void ClearPreRoll()
    {
        while (preRoll.TryDequeue(out var frame)) ArrayPool<byte>.Shared.Return(frame.Buffer);
        preRollBytes = 0;
    }
}
