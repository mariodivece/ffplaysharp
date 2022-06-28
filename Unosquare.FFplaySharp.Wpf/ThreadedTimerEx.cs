namespace Unosquare.FFplaySharp.Wpf;

internal class ThreadedTimerEx
{
    private Task? WorkerTask;
    private readonly CancellationTokenSource Cts = new();

    private bool IsDisposed;
    public event EventHandler? Elapsed;

    public ThreadedTimerEx(int intervalMillis = 1)
    {
        Interval = TimeSpan.FromMilliseconds(intervalMillis);
    }

    public bool IsRunning { get; private set; }

    public TimeSpan Interval { get; }

    public int Resolution { get; }

    public void Start()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        WorkerTask = WorkerLoop();
    }

    private async Task WorkerLoop()
    {
        using var timer = new PeriodicTimer(Interval);
        var token = Cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (await timer.WaitForNextTickAsync(token).ConfigureAwait(false) == false)
                    break;

                Elapsed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        finally
        {
            timer.Dispose();
            IsRunning = false;
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Cts.Cancel();

        if (WorkerTask is not null)
        {
            while (!WorkerTask.IsCompleted)
                WorkerTask.Wait();
        }

        Cts.Dispose();
    }
}
