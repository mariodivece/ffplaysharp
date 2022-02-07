namespace Unosquare.FFplaySharp.Wpf;

public class MultimediaStopwatch
{
    private double BaseTime;

    public MultimediaStopwatch()
    {

    }

    public bool IsRunning => Interlocked.CompareExchange(ref BaseTime, 0, 0) != 0;

    public double ElapsedSeconds => Clock.SystemTime - Interlocked.CompareExchange(ref BaseTime, 0, 0);

    public void Restart() => Interlocked.Exchange(ref BaseTime, Clock.SystemTime);

    public void Restart(double offset) =>
        Interlocked.Exchange(ref BaseTime, Clock.SystemTime - offset);
}
