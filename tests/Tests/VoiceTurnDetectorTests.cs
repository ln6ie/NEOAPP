using NEO.Core;

namespace NEO.Tests;

public sealed class VoiceTurnDetectorTests
{
    [Fact]
    public void SilenceAloneDoesNotGenerateResponses()
    {
        var detector = new VoiceTurnDetector();
        for (var i = 0; i < 1000; i++) Assert.False(detector.Observe(0, 20));
    }

    [Fact]
    public void SpeechFollowedBySilenceEndsTurnWithoutStopButton()
    {
        var detector = new VoiceTurnDetector();
        detector.Observe(0.0001f, 300);
        for (var i = 0; i < 15; i++) Assert.False(detector.Observe(0.1f, 20));
        for (var i = 0; i < 32; i++) Assert.False(detector.Observe(0, 20));
        Assert.True(detector.Observe(0, 20));
        detector.Reset();
        Assert.False(detector.Observe(0, 1000));
    }

    [Fact]
    public void QuietMicrophoneFromReportedLogTriggersTurn()
    {
        var detector = new VoiceTurnDetector();
        detector.Observe(0.00012f, 300);
        for (var i = 0; i < 15; i++) Assert.False(detector.Observe(0.004f, 20));
        Assert.True(detector.IsSpeechActive);
        Assert.True(detector.Observe(0.0001f, 660));
    }

    [Fact]
    public void SteadyBackgroundNoiseDoesNotTriggerSpeech()
    {
        var detector = new VoiceTurnDetector();
        for (var i = 0; i < 1000; i++) Assert.False(detector.Observe(0.002f, 20));
        Assert.False(detector.IsSpeechActive);
    }
}
