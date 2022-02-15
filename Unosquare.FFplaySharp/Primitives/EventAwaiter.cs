namespace Unosquare.FFplaySharp.Primitives;

internal class EventAwaiter
{
    private readonly object SyncLock = new();
    private readonly List<int> WaitQueue = new(32);

    public void Signal()
    {
        lock (SyncLock)
        {
            if (WaitQueue.Count > 0)
                WaitQueue.RemoveAt(0);
        }
    }

    public void SignalAll()
    {
        lock (SyncLock)
        {
            WaitQueue.Clear();
        }
    }

    public bool Wait(int millisecondsTimeout)
    {
        var startTime = Clock.SystemTime;
        var timeout = millisecondsTimeout / 1000d;

        var id = -1;
        lock (SyncLock)
        {
            id = WaitQueue.Count;
            WaitQueue.Add(id);
        }

        while (true)
        {
            lock (SyncLock)
            {
                if (!WaitQueue.Contains(id))
                    return true;
            }

            if (Clock.SystemTime - startTime > timeout)
                break;

            Thread.Sleep(1);
        }

        return false;
    }
}
