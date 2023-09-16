using Unosquare.Hpet;

namespace Unosquare.FFplaySharp.Primitives;

internal class EventAwaiter
{
    private static readonly TimeSpan LoopingTimeout = TimeSpan.FromMilliseconds(1);

    private readonly EventQueue WaitQueue = new();

    public void Signal() => WaitQueue.Dequeue();

    public void SignalAll() => WaitQueue.Clear();

    public bool Wait(int millisecondsTimeout)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var id = WaitQueue.Enqueue();

        while (true)
        {
            if (!WaitQueue.Contains(id))
                return true;

            if (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds >= millisecondsTimeout)
                break;

            LoopingTimeout.Delay();
        }

        WaitQueue.Remove(id);
        return false;
    }

    private sealed class EventQueue
    {
        private readonly object SyncLock = new();
        private readonly SortedList<int, int> WaitQueue = new();

        public int Enqueue()
        {
            lock (SyncLock)
            {
                var id = WaitQueue.Count;
                WaitQueue[id] = default;
                return id;
            }
        }

        public void Dequeue()
        {
            lock (SyncLock)
            {
                if (WaitQueue.Count <= 0)
                    return;

                WaitQueue.RemoveAt(0);
            }
        }

        public void Remove(int id)
        {
            lock (SyncLock)
            {
                if (!WaitQueue.ContainsKey(id))
                    return;

                WaitQueue.Remove(id);
            }
        }

        public bool Contains(int id)
        {
            lock (SyncLock)
                return WaitQueue.ContainsKey(id);
        }

        public void Clear()
        {
            lock (SyncLock)
                WaitQueue.Clear();
        }
    }
}
