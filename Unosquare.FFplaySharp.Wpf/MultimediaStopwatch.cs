namespace Unosquare.FFplaySharp.Wpf;

public class MultimediaStopwatch
{
    private static readonly double SecondsPerTick = 1f / Stopwatch.Frequency;

    private double BaseTime;

    public MultimediaStopwatch()
    {

    }

    public bool IsRunning => Interlocked.CompareExchange(ref BaseTime, 0, 0) != 0;

    public double ElapsedSeconds => (SecondsPerTick * Stopwatch.GetTimestamp()) - Interlocked.CompareExchange(ref BaseTime, 0, 0);

    public void Restart() => Interlocked.Exchange(ref BaseTime, SecondsPerTick * Stopwatch.GetTimestamp());

    public void Restart(double offset) =>
        Interlocked.Exchange(ref BaseTime, (SecondsPerTick * Stopwatch.GetTimestamp()) - offset);
}
