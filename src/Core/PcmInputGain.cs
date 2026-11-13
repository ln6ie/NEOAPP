namespace NEO.Core;

public sealed class PcmInputGain
{
    public float Gain { get; private set; } = 1;
    public void Reset() => Gain = 1;
    public void Apply(Span<byte> pcm)
    {
        if (pcm.Length < 2) return;
        double sum = 0, peak = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var value = (short)(pcm[i] | pcm[i + 1] << 8) / 32768d;
            sum += value * value; peak = Math.Max(peak, Math.Abs(value));
        }
        var rms = Math.Sqrt(sum / (pcm.Length / 2));
        // Adapt only to speech-level energy; quiet turn tails must not drive AGC up.
        if (rms < 0.0003) return;
        var target = Math.Min(8, Math.Min(0.06 / rms, 0.95 / Math.Max(peak, 0.00001)));
        Gain = (float)Math.Min(target, Gain * 0.7 + target * 0.3);
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var value = (short)(pcm[i] | pcm[i + 1] << 8);
            var amplified = (short)Math.Clamp((int)Math.Round(value * Gain), -32767, 32767);
            pcm[i] = (byte)amplified; pcm[i + 1] = (byte)(amplified >> 8);
        }
    }
}
