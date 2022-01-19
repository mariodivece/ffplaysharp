namespace Unosquare.FFplaySharp.Primitives;

public sealed class FrameQueue : IDisposable
{
    private readonly object SyncLock = new();
    private readonly AutoResetEvent ChangedEvent = new(false);
    private readonly FrameHolder[] Frames;
    private readonly PacketQueue Packets;

    private long m_IsReadIndexShown;
    private long m_ReadIndex;
    private long m_WriteIndex;
    private long m_Count;

    public FrameQueue(PacketQueue packets, int capacity, bool keepLast)
    {
        var capacityLimit = Math.Max(
            Constants.AudioFrameQueueCapacity, Math.Max(
                Constants.VideoFrameQueueCapacity, Constants.SubtitleFrameQueueCapacity));

        Packets = packets;
        Capacity = Math.Min(capacity, capacityLimit);
        KeepLast = keepLast;

        Frames = new FrameHolder[Capacity];
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new FrameHolder();
    }

    public bool IsReadIndexShown
    {
        get => Interlocked.Read(ref m_IsReadIndexShown) != 0;
        private set => Interlocked.Exchange(ref m_IsReadIndexShown, value ? 1 : 0);
    }

    public int Capacity { get; }

    public bool KeepLast { get; }

    public int ReadIndex
    {
        get => (int)Interlocked.Read(ref m_ReadIndex);
        private set => Interlocked.Exchange(ref m_ReadIndex, value);
    }

    public int WriteIndex
    {
        get => (int)Interlocked.Read(ref m_WriteIndex);
        private set => Interlocked.Exchange(ref m_WriteIndex, value);
    }

    public int Count
    {
        get => (int)Interlocked.Read(ref m_Count);
        private set => Interlocked.Exchange(ref m_Count, value);
    }

    public void SignalChanged() => ChangedEvent.Set();

    public FrameHolder? PeekWriteable()
    {
        // wait until we have space to put a new frame
        while (Count >= Capacity && !Packets.IsClosed)
            ChangedEvent.WaitOne();

        lock (SyncLock)
        {
            if (Packets.IsClosed)
                return default;
            else
                return Frames[WriteIndex];
        }
    }

    public FrameHolder PeekCurrent()
    {
        lock (SyncLock)
            return Frames[(ReadIndex + (IsReadIndexShown ? 1 : 0)) % Capacity];
    }

    public FrameHolder? PeekWaitCurrent()
    {
        // wait until we have a readable a new frame
        while (Count - (IsReadIndexShown ? 1 : 0) <= 0 && !Packets.IsClosed)
            ChangedEvent.WaitOne();

        return Packets.IsClosed
            ? default
            : PeekCurrent();
    }

    public FrameHolder PeekNext()
    {
        lock (SyncLock)
            return Frames[(ReadIndex + (IsReadIndexShown ? 1 : 0) + 1) % Capacity];
    }

    public FrameHolder PeekPrevious()
    {
        lock (SyncLock)
            return Frames[ReadIndex];
    }

    public void Enqueue()
    {
        lock (SyncLock)
        {
            if (++WriteIndex >= Capacity)
                WriteIndex = 0;

            Count++;
        }

        ChangedEvent.Set();
    }

    public void Dequeue()
    {
        lock (SyncLock)
        {
            if (KeepLast && !IsReadIndexShown)
            {
                IsReadIndexShown = true;
                return;
            }

            Frames[ReadIndex].Reset();
            if (++ReadIndex >= Capacity)
                ReadIndex = 0;

            Count--;
        }

        ChangedEvent.Set();
    }

    /// <summary>
    /// Gets the number the number of undisplayed frames in the queue.
    /// </summary>
    public int PendingCount => Count - (IsReadIndexShown ? 1 : 0);

    /// <summary>
    /// Gets the last shown byte position within the stream.
    /// </summary>
    public long ShownBytePosition
    {
        get
        {
            lock (SyncLock)
            {
                var currentFrame = Frames[ReadIndex];
                if (IsReadIndexShown && currentFrame.GroupIndex == Packets.GroupIndex)
                    return currentFrame.Frame.PacketPosition;
                else
                    return -1;
            }
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < Frames.Length; i++)
        {
            Frames[i].Dispose();
            Frames[i] = default;
        }

        ChangedEvent.Dispose();
    }
}
