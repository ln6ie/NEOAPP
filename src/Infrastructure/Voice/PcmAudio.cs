using NAudio.Wave;

namespace NEO.Infrastructure;

internal static class PcmAudio
{
    public static byte[] ToMono16(byte[] buffer, int count, WaveFormat? format, int sampleRate)
    {
        if (format is null) return [];
        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16 && format.Channels == 1 && format.SampleRate == sampleRate)
        {
            var result = new byte[count];
            Buffer.BlockCopy(buffer, 0, result, 0, count);
            return result;
        }

        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var frames = count / (bytesPerSample * channels);
        var mono = new byte[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = (frame * channels + channel) * bytesPerSample;
                sum += format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample >= 4
                    ? BitConverter.ToSingle(buffer, offset)
                    : BitConverter.ToInt16(buffer, offset) / 32768f;
            }
            var sample = (short)Math.Clamp(sum / channels * 32767f, short.MinValue, short.MaxValue);
            mono[frame * 2] = (byte)sample;
            mono[frame * 2 + 1] = (byte)(sample >> 8);
        }
        return mono;
    }

    public static float Level(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2) return 0;
        double sum = 0;
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var sample = (short)(bytes[i] | bytes[i + 1] << 8) / 32768d;
            sum += sample * sample;
        }
        return (float)Math.Clamp(Math.Sqrt(sum / (bytes.Length / 2)) * 3.2, 0, 1);
    }
}
