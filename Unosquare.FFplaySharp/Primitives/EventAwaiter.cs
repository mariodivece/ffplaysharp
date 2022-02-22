namespace Unosquare.FFplaySharp.Primitives;

internal class EventAwaiter
{
    private readonly EventQueue WaitQueue = new();

    public void Signal() => WaitQueue.Dequeue();

    public void SignalAll() => WaitQueue.Clear();

    public bool Wait(int millisecondsTimeout)
    {
        var startTimeUs = ffmpeg.av_gettime_relative();
        var timeoutUs = millisecondsTimeout * 1000L;
        var maxSleepUs = millisecondsTimeout * 500L;
        long remainingUs;

        var id = WaitQueue.Enqueue();

        do
        {
            if (!WaitQueue.Contains(id))
                return true;

            remainingUs = timeoutUs - (ffmpeg.av_gettime_relative() - startTimeUs);
            if (remainingUs > 0)
                ffmpeg.av_usleep(Convert.ToUInt32(remainingUs.Clamp(1, maxSleepUs)));

        } while (remainingUs > 0);

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
