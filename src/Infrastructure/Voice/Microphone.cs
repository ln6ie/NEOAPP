using System.Buffers;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace NEO.Infrastructure;

internal sealed class Microphone(string? deviceId, Func<AudioChunk, Task> onFrame, Action<Exception>? onError = null)
{
    private const int Rate = 16000;
    private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
    private MMDevice? endpoint;
    private WasapiCapture? capture;
    private Channel<AudioChunk>? frames;
    private CancellationTokenSource? lifetime;
    private Task? pump;
    private long captured;
    private float peak;
    public long CapturedBytes => Interlocked.Read(ref captured);
    public float PeakLevel => peak;
    public event Action<float>? LevelChanged;

    public void Start()
    {
        if (capture != null) return;
        try
        {
            using var devices = new MMDeviceEnumerator();
            endpoint = string.IsNullOrEmpty(deviceId) ? devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : devices.GetDevice(deviceId);
            NEOLog.Write("mic.device", new { name = endpoint.FriendlyName, id = endpoint.ID, muted = endpoint.AudioEndpointVolume.Mute, volume = endpoint.AudioEndpointVolume.MasterVolumeLevelScalar });
            if (endpoint.AudioEndpointVolume.Mute) throw new InvalidOperationException("The selected microphone is muted in Windows. Unmute it or select another microphone in NEO settings.");
            capture = new WasapiCapture(endpoint) { WaveFormat = new WaveFormat(Rate, 16, 1) };
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            frames = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
            lifetime = new CancellationTokenSource();
            pump = PumpAsync(frames.Reader, lifetime.Token);
            NEOLog.Write("mic.start", new { format = capture.WaveFormat.ToString(), targetRate = Rate });
            capture.StartRecording();
        }
        catch
        {
            capture?.Dispose(); capture = null;
            endpoint?.Dispose(); endpoint = null;
            frames?.Writer.TryComplete(); frames = null;
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
            pump = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        var current = Interlocked.Exchange(ref capture, null);
        if (current != null)
        {
            current.DataAvailable -= OnDataAvailable;
            current.RecordingStopped -= OnRecordingStopped;
            try { current.StopRecording(); } catch (InvalidOperationException) { }
            current.Dispose();
        }
        endpoint?.Dispose(); endpoint = null;
        frames?.Writer.TryComplete();
        try { if (pump != null) await pump.ConfigureAwait(false); } catch (Exception ex) { onError?.Invoke(ex); }
        pump = null; frames = null;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
        NEOLog.Write("mic.stopped", new { CapturedBytes, PeakLevel });
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null) onError?.Invoke(e.Exception);
        NEOLog.Write("mic.recording_stopped", new { failed = e.Exception != null });
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var writer = frames?.Writer;
        if (e.BytesRecorded <= 0 || writer == null) return;
        var bytes = PcmAudio.ToMono16(e.Buffer, e.BytesRecorded, capture?.WaveFormat, Rate);
        if (bytes.Length == 0) return;
        Interlocked.Add(ref captured, bytes.Length);
        var level = PcmAudio.Level(bytes);
        peak = Math.Max(peak, level);
        LevelChanged?.Invoke(level);
        var rented = pool.Rent(bytes.Length);
        bytes.CopyTo(rented, 0);
        if (!writer.TryWrite(new AudioChunk(rented, bytes.Length)))
        {
            pool.Return(rented);
            NEOLog.Write("mic.queue.full", new { lostBytes = bytes.Length });
        }
    }

    private async Task PumpAsync(ChannelReader<AudioChunk> reader, CancellationToken ct)
    {
        try { await foreach (var frame in reader.ReadAllAsync(ct).ConfigureAwait(false)) await onFrame(frame).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { while (reader.TryRead(out var pending)) pool.Return(pending.Buffer); }
    }
}

internal sealed record AudioChunk(byte[] Buffer, int Count);
