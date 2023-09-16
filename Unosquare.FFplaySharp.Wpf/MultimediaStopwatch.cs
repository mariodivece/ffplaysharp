namespace Unosquare.FFplaySharp.Wpf;

public class MultimediaStopwatch
{
    private long BaseTime;

    public MultimediaStopwatch()
    {

    }

    public bool IsRunning => Interlocked.Read(ref BaseTime) != 0;

    public double ElapsedSeconds => Stopwatch.GetElapsedTime(BaseTime).TotalSeconds;

    public double Restart() => Stopwatch.GetElapsedTime(Interlocked.Exchange(ref BaseTime, Stopwatch.GetTimestamp())).TotalSeconds;
}
