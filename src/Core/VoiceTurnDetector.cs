namespace NEO.Core;

// Counts audio time rather than wall time so scheduling delays cannot end a turn.
public sealed class VoiceTurnDetector
{
    private double speechMilliseconds;
    private double silenceMilliseconds;
    private double turnMilliseconds;
    private double calibrationMilliseconds;
    private float noiseFloor = 0.0001f;
    private readonly double requiredSilence;
    public VoiceTurnDetector(double silenceDuration = 650) => requiredSilence = silenceDuration;
    public float Threshold => Math.Max(0.0008f, noiseFloor * 4);
    public bool IsSpeechActive => speechMilliseconds >= 120;

    public bool Observe(float level, double milliseconds)
    {
        if (calibrationMilliseconds < 300)
        {
            noiseFloor = Math.Max(noiseFloor, level);
            calibrationMilliseconds += milliseconds;
            return false;
        }
        if (speechMilliseconds > 0) turnMilliseconds += milliseconds;
        if (level >= Threshold)
        {
            speechMilliseconds += milliseconds;
            silenceMilliseconds = 0;
        }
        else if (IsSpeechActive) silenceMilliseconds += milliseconds;
        else
        {
            speechMilliseconds = 0;
            noiseFloor = noiseFloor * 0.98f + level * 0.02f;
        }
        return speechMilliseconds >= 120 && (silenceMilliseconds >= requiredSilence || turnMilliseconds >= 30000);
    }

    public void Reset() => speechMilliseconds = silenceMilliseconds = turnMilliseconds = 0;
}
