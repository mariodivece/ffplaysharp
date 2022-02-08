namespace Unosquare.FFplaySharp.Wpf;

public sealed class ThreadedTimer : IDisposable
{
    private readonly Thread Worker;
    private bool IsDisposed;
    private CancellationTokenSource Cts = new();
    public event EventHandler? Elapsed;

    public ThreadedTimer(int intervalMillis = 1)
    {
        Worker = new Thread(WorkerLoop) { IsBackground = true };
        Interval = TimeSpan.FromMilliseconds(intervalMillis);
    }

    public bool IsRunning { get; private set; }

    public TimeSpan Interval { get; }

    public void Start()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        Worker.Start();
    }

    private void WorkerLoop()
    {
        var token = Cts.Token;
        var currentInterval = Interval.TotalSeconds;
        var cycleClock = new MultimediaStopwatch();
        cycleClock.Restart(currentInterval);

        while (!token.IsCancellationRequested)
        {
            if (cycleClock.ElapsedSeconds < currentInterval)
            {
                Thread.Sleep(1);
                continue;
            }

            cycleClock.Restart();

            if (token.IsCancellationRequested)
                return;

            Elapsed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Cts.Cancel();
        Worker.Join();
        Cts.Dispose();
    }
}
