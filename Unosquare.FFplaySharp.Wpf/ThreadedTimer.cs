using System.Runtime.InteropServices;

namespace Unosquare.FFplaySharp.Wpf;

public sealed class ThreadedTimer : IDisposable
{
    private readonly Thread Worker;
    private readonly CancellationTokenSource Cts = new();

    private bool IsDisposed;
    public event EventHandler? Elapsed;

    public ThreadedTimer(int intervalMillis = 1, int resolution = 1)
    {
        Worker = new Thread(WorkerLoop) { IsBackground = true };
        Interval = TimeSpan.FromMilliseconds(intervalMillis);
        Resolution = resolution;
    }

    public bool IsRunning { get; private set; }

    public TimeSpan Interval { get; }

    public int Resolution { get; }

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
        cycleClock.Restart();
        var resolutionMillis = (uint)Math.Max(1, Resolution);
        try
        {
            _ = NativeMethods.BeginTimerResolution(resolutionMillis);
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
        finally
        {
            IsRunning = false;
            _ = NativeMethods.EndTimerResolution((uint)Resolution);
            Cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Cts.Cancel();

        if (!IsRunning)
            Cts.Dispose();
    }

    private static class NativeMethods
    {
        private const string MultimediaDll = "Winmm.dll";

        [DllImport(MultimediaDll, SetLastError = true, EntryPoint = "timeBeginPeriod")]
        public static extern uint BeginTimerResolution(uint value);

        [DllImport(MultimediaDll, SetLastError = true, EntryPoint = "timeEndPeriod")]
        public static extern uint EndTimerResolution(uint value);
    }
}
