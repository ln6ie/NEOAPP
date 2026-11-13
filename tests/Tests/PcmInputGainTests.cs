using NEO.Core;

namespace NEO.Tests;

public sealed class PcmInputGainTests
{
    [Fact]
    public void QuietTailDoesNotPumpGainOrAmplifyNoise()
    {
        var gain = new PcmInputGain();
        var quiet = new byte[320];
        for (var i = 0; i < quiet.Length; i += 2) quiet[i] = 3;
        for (var n = 0; n < 100; n++) gain.Apply(quiet);
        Assert.Equal(1, gain.Gain);
        Assert.Equal(3, BitConverter.ToInt16(quiet, 0));
    }
    [Fact]
    public void QuietSpeechIsBoostedWithoutChangingSampleCount()
    {
        var pcm = new byte[320];
        for (var i = 0; i < pcm.Length; i += 2) { pcm[i] = 32; pcm[i + 1] = 0; }
        new PcmInputGain().Apply(pcm);
        Assert.True(BitConverter.ToInt16(pcm, 0) > 32);
        Assert.Equal(320, pcm.Length);
    }
    [Fact]
    public void SilenceStaysSilentAndPeaksDoNotClip()
    {
        var gain = new PcmInputGain();
        var pcm = new byte[320]; gain.Apply(pcm); Assert.All(pcm, b => Assert.Equal(0, b));
        for (var i = 0; i < pcm.Length; i += 2) { pcm[i] = 0xF8; pcm[i + 1] = 0x7F; }
        gain.Apply(pcm);
        Assert.InRange(BitConverter.ToInt16(pcm, 0), 1, 32767);
    }
}
