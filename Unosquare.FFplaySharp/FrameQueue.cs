namespace Unosquare.FFplaySharp
{
    using System;
    using System.Threading;

    public unsafe class FrameQueue : IDisposable
    {
        private readonly object SyncLock = new();
        private readonly AutoResetEvent ChangedEvent = new(false);
        private readonly FrameHolder[] queue;
        private readonly PacketQueue Packets;
        private readonly int MaxSize;
        private readonly bool KeepLast;

        private int ReadIndex;
        private int WriteIndex;
        private int Size;

        public FrameQueue(PacketQueue packets, int maxSize, bool keepLast)
        {
            queue = new FrameHolder[Constants.FRAME_QUEUE_SIZE];
            for (var i = 0; i < queue.Length; i++)
                queue[i] = new FrameHolder();

            Packets = packets;
            MaxSize = Math.Min(maxSize, Constants.FRAME_QUEUE_SIZE);
            KeepLast = keepLast;
        }

        public bool ReadIndexShown { get; private set; }

        public void SignalChanged()
        {
            ChangedEvent.Set();
        }

        public FrameHolder PeekWriteable()
        {
            /* wait until we have space to put a new frame */
            while (Size >= MaxSize && !Packets.IsClosed)
            {
                ChangedEvent.WaitOne(); // 10);
            }

            if (Packets.IsClosed)
                return null;

            lock (SyncLock)
                return queue[WriteIndex];
        }

        public FrameHolder Peek()
        {
            lock (SyncLock)
                return queue[(ReadIndex + (ReadIndexShown ? 1 : 0)) % MaxSize];
        }

        public FrameHolder PeekNext()
        {
            lock (SyncLock)
                return queue[(ReadIndex + (ReadIndexShown ? 1 : 0) + 1) % MaxSize];
        }

        public FrameHolder PeekLast()
        {
            lock (SyncLock)
                return queue[ReadIndex];
        }

        public FrameHolder PeekReadable()
        {
            /* wait until we have a readable a new frame */
            while (Size - (ReadIndexShown ? 1 : 0) <= 0 && !Packets.IsClosed)
            {
                ChangedEvent.WaitOne(); //10);
            }

            if (Packets.IsClosed)
                return null;

            lock (SyncLock)
                return queue[(ReadIndex + (ReadIndexShown ? 1 : 0)) % MaxSize];
        }

        public void Push()
        {
            lock (SyncLock)
            {
                if (++WriteIndex == MaxSize)
                    WriteIndex = 0;
                Size++;
            }

            ChangedEvent.Set();
        }

        public void Next()
        {
            lock (SyncLock)
            {
                if (KeepLast && !ReadIndexShown)
                {
                    ReadIndexShown = true;
                    return;
                }

                queue[ReadIndex].Unreference();
                if (++ReadIndex == MaxSize)
                    ReadIndex = 0;

                Size--;
            }

            ChangedEvent.Set();
        }

        /* return the number of undisplayed frames in the queue */
        public int PendingCount
        {
            get
            {
                lock (SyncLock)
                    return Size - (ReadIndexShown ? 1 : 0);
            }
        }

        /* return last shown position */
        public long LastPosition
        {
            get
            {
                lock (SyncLock)
                {
                    var fp = queue[ReadIndex];
                    if (ReadIndexShown && fp.Serial == Packets.Serial)
                        return fp.Position;
                    else
                        return -1;
                }
            }
        }

        public void Dispose()
        {
            for (var i = 0; i < MaxSize; i++)
            {
                queue[i].Dispose();
                queue[i] = null;
            }

            ChangedEvent.Dispose();
        }
    }
}
