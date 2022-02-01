using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Unosquare.FFplaySharp.Wpf;

public sealed class MultimediaTimer : IDisposable
{
    private delegate void MultimediaTimerCallback(uint timerId, uint message, ref uint userContext, uint reservedA, uint reservedB);

    // Hold the timer callback to prevent garbage collection.
    private readonly MultimediaTimerCallback timerCallback;
    private readonly uint interval;
    private readonly uint resolution;

    private bool disposed;
    private uint timerId;

    public event EventHandler? Elapsed;

    public MultimediaTimer(int intervalMillis = 10, int resolutionMillis = 5)
    {
        timerCallback = TimerCallbackMethod;
        interval = intervalMillis < 1
            ? 1
            : Convert.ToUInt32(intervalMillis);

        resolution = resolutionMillis > interval
            ? interval
            : resolutionMillis < 0
            ? 0
            : Convert.ToUInt32(resolutionMillis);
    }

    ~MultimediaTimer() => Dispose(false);

    public int Interval => Convert.ToInt32(interval);

    public int Resolution => Convert.ToInt32(resolution);

    public bool IsRunning => timerId != 0;

    public void Start()
    {
        // Event type = 0, one off event
        // Event type = 1, periodic event
        const int PeriodicMode = 1;

        CheckDisposed();

        if (IsRunning)
            throw new InvalidOperationException("Timer is already running.");

        uint userCtx = 0;
        timerId = NativeMethods.TimeSetEvent(
            interval, resolution, timerCallback, ref userCtx, PeriodicMode);

        if (timerId is 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Stop()
    {
        CheckDisposed();

        if (!IsRunning)
            throw new InvalidOperationException("Timer has not been started");

        StopInternal();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposed)
            return;

        disposed = true;
        if (IsRunning)
            StopInternal();

        if (disposing)
            Elapsed = null;
    }

    private void CheckDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(MultimediaTimer));
    }

    private void StopInternal()
    {
        NativeMethods.TimeKillEvent(timerId);
        timerId = 0;
    }

    private void TimerCallbackMethod(uint timerId, uint message, ref uint userContext, uint reservedA, uint reservedB)
    {
        Elapsed?.Invoke(this, EventArgs.Empty);
    }

    private static class NativeMethods
    {
        private const string MultimediaDll = "Winmm.dll";

        [DllImport(MultimediaDll, SetLastError = true, EntryPoint = "timeSetEvent")]
        public static extern uint TimeSetEvent(
            uint delayMillis, uint resolutionMillis, MultimediaTimerCallback callback, ref uint userContext, uint eventType);

        [DllImport(MultimediaDll, SetLastError = true, EntryPoint = "timeKillEvent")]
        public static extern void TimeKillEvent(uint timerId);
    }
}
