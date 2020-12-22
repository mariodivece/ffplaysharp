namespace Unosquare.FFplaySharp.Primitives
{
    using System;
    using System.Threading;

    public unsafe sealed class FrameQueue : IDisposable
    {
        private readonly object SyncLock = new();
        private readonly AutoResetEvent ChangedEvent = new(false);
        private readonly FrameHolder[] Frames;
        private readonly PacketQueue Packets;

        private bool m_IsReadIndexShown;
        private int m_ReadIndex;
        private int m_WriteIndex;
        private int m_Size;

        public FrameQueue(PacketQueue packets, int maxSize, bool keepLast)
        {
            Frames = new FrameHolder[Constants.FRAME_QUEUE_SIZE];
            for (var i = 0; i < Frames.Length; i++)
                Frames[i] = new FrameHolder();

            Packets = packets;
            MaxSize = Math.Min(maxSize, Constants.FRAME_QUEUE_SIZE);
            KeepLast = keepLast;
        }

        public bool IsReadIndexShown
        {
            get { lock (SyncLock) return m_IsReadIndexShown; }
            private set { lock (SyncLock) m_IsReadIndexShown = value; }
        }

        public int MaxSize { get; }

        public bool KeepLast { get; }

        public int ReadIndex
        {
            get { lock (SyncLock) return m_ReadIndex; }
            private set { lock (SyncLock) m_ReadIndex = value; }
        }

        public int WriteIndex
        {
            get { lock (SyncLock) return m_WriteIndex; }
            private set { lock (SyncLock) m_WriteIndex = value; }
        }

        public int Size
        {
            get { lock (SyncLock) return m_Size; }
            private set { lock (SyncLock) m_Size = value; }
        }

        public void SignalChanged() => ChangedEvent.Set();

        public FrameHolder PeekWriteable()
        {
            /* wait until we have space to put a new frame */
            while (Size >= MaxSize && !Packets.IsClosed)
                ChangedEvent.WaitOne(10);

            lock (SyncLock)
            {
                if (Packets.IsClosed)
                    return null;
                else
                    return Frames[WriteIndex];
            }
        }

        public FrameHolder Peek()
        {
            lock (SyncLock)
                return Frames[(ReadIndex + (IsReadIndexShown ? 1 : 0)) % MaxSize];
        }

        public FrameHolder PeekNext()
        {
            lock (SyncLock)
                return Frames[(ReadIndex + (IsReadIndexShown ? 1 : 0) + 1) % MaxSize];
        }

        public FrameHolder PeekLast()
        {
            lock (SyncLock)
                return Frames[ReadIndex];
        }

        public FrameHolder PeekReadable()
        {
            /* wait until we have a readable a new frame */
            while (Size - (IsReadIndexShown ? 1 : 0) <= 0 && !Packets.IsClosed)
                ChangedEvent.WaitOne(10);

            if (Packets.IsClosed)
                return null;

            lock (SyncLock)
                return Frames[(ReadIndex + (IsReadIndexShown ? 1 : 0)) % MaxSize];
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
                if (KeepLast && !IsReadIndexShown)
                {
                    IsReadIndexShown = true;
                    return;
                }

                Frames[ReadIndex].Unreference();
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
                    return Size - (IsReadIndexShown ? 1 : 0);
            }
        }

        /* return last shown position */
        public long LastPosition
        {
            get
            {
                lock (SyncLock)
                {
                    var fp = Frames[ReadIndex];
                    if (IsReadIndexShown && fp.Serial == Packets.Serial)
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
                Frames[i].Dispose();
                Frames[i] = null;
            }

            ChangedEvent.Dispose();
        }
    }
}
