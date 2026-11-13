using NAudio.Wave;

namespace NEO.Infrastructure;

// A bounded speaker queue: producers wait for the audio device, never drop PCM.
public sealed class PcmPlaybackBuffer : IWaveProvider
{
    private readonly BufferedWaveProvider buffer;
    private long consumedBytes;

    public PcmPlaybackBuffer(int sampleRate)
    {
        buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = false
        };
    }

    public WaveFormat WaveFormat => buffer.WaveFormat;
    public int BufferedBytes => buffer.BufferedBytes;
    public long ConsumedBytes => Interlocked.Read(ref consumedBytes);
    public void ClearBuffer() => buffer.ClearBuffer();
    public int Read(byte[] bytes, int offset, int count)
    {
        var available = Math.Min(count, buffer.BufferedBytes);
        var read = buffer.Read(bytes, offset, count);
        Interlocked.Add(ref consumedBytes, available);
        return read;
    }

    public async Task EnqueueAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length % WaveFormat.BlockAlign != 0)
            throw new ArgumentException("PCM must contain complete sample frames.", nameof(bytes));
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(bytes.Length - offset, buffer.BufferLength - buffer.BufferedBytes);
            count -= count % WaveFormat.BlockAlign;
            if (count > 0)
            {
                buffer.AddSamples(bytes, offset, count);
                offset += count;
            }
            else await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }
}
