namespace Unosquare.FFplaySharp.Primitives;

/// <summary>
/// Represents a queue-style data structure used to read and write media frames.
/// Internally, it is implemented with 2 queues. One with readable frames and
/// one with writable frames. Writable frames are leased, and then equeued on to
/// the readable frame queue. Readable frames are peeeked and then dequeued and back
/// on to the writable frames queue.
/// </summary>
public sealed class FrameStore : IDisposable, ISerialGroupable
{
    private readonly AutoResetEvent ChangedEvent = new(false);
    private readonly FrameHolderQueue WritableFrames;
    private readonly FrameHolderQueue ReadableFrames;
    private readonly PacketStore Packets;

    private bool isDisposed;
    private long m_IsReadIndexShown;

    /// <summary>
    /// Creates a new instance of the <see cref="FrameStore"/> class.
    /// </summary>
    /// <param name="packets">The underlying packet queue.</param>
    /// <param name="capacity">The total capacity in frames to allocate.</param>
    /// <param name="keepLast">Prevents the last frame from being dequeued if it has not been shown.</param>
    public FrameStore(PacketStore packets, int capacity, bool keepLast)
    {
        var capacityLimit = Math.Max(
            Constants.AudioFrameQueueCapacity, Math.Max(
                Constants.VideoFrameQueueCapacity, Constants.SubtitleFrameQueueCapacity));

        Packets = packets;
        Capacity = Math.Min(capacity, capacityLimit);
        KeepLast = keepLast;
        ReadableFrames = new(Capacity, false);
        WritableFrames = new(Capacity, true);
    }

    /// <summary>
    /// Gets the total capacity of this frame queue.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets a value indicating whether to keep a last frame
    /// available it has not been shown yet but it is being dequeued.
    /// </summary>
    public bool KeepLast { get; }

    /// <inheritdoc />
    public int GroupIndex => Packets.GroupIndex;

    /// <summary>
    /// Gets a value indicating if the underlying
    /// packet queue is closed.
    /// </summary>
    public bool IsClosed => isDisposed || Packets.IsClosed;

    /// <summary>
    /// Gets a value indicating if the current radable frame is marked as shown.
    /// </summary>
    public bool IsReadIndexShown
    {
        get => Interlocked.Read(ref m_IsReadIndexShown) != 0L;
        private set => Interlocked.Exchange(ref m_IsReadIndexShown, value ? 1L : 0L);
    }

    /// <summary>
    /// Gets the number the number of undisplayed frames in the queue.
    /// Port of frame_queue_nb_remaining.
    /// </summary>
    public int PendingCount => Math.Max(0, ReadableFrames.Count - Convert.ToInt32(IsReadIndexShown));

    /// <summary>
    /// Gets a value indicating if <see cref="PendingCount"/> is greater than 0.
    /// </summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>
    /// Gets the last shown byte position within the stream.
    /// Port of frame_queue_last_pos.
    /// </summary>
    public long ShownBytePosition => IsReadIndexShown && ReadableFrames.TryPeek(out var item) && item.GroupIndex == GroupIndex
        ? item.Frame.PacketPosition
        : -1;

    /// <summary>
    /// Forces change event to be signalled.
    /// Port of frame_queue_signal.
    /// </summary>
    public void SignalChanged() => ChangedEvent.Set();

    /// <summary>
    /// Gets the next available writable frame to be written into.
    /// This method will block until the queue if full of readable frames,
    /// and return null if the queue is closed.
    /// Port of frame_queue_peek_writable.
    /// </summary>
    /// <returns>The frame.</returns>
    public bool LeaseFrameForWriting([MaybeNullWhen(false)] out FrameHolder frame)
    {
        // wait until we have space to put a new frame
        // that is, our readable count is less than
        // the capacity of the queue.
        while (!IsClosed && WritableFrames.IsEmpty)
            ChangedEvent.WaitOne(Constants.WaitTimeout, true);

        frame = default;
        return !IsClosed && WritableFrames.TryPeek(out frame);
    }

    /// <summary>
    /// After obtaining a writable frame by calling
    /// <see cref="LeaseFrameForWriting"/> and writing to it,
    /// call this method to commit it and make such frame readable.
    /// Port of frame_queue_push.
    /// </summary>
    public void EnqueueLeasedFrame()
    {
        if (WritableFrames.TryDequeue(out var frame))
            ReadableFrames.Enqueue(frame);

        ChangedEvent.Set();
    }

    /// <summary>
    /// Gets the next available readable frame for showing.
    /// Port of frame_queue_peek.
    /// </summary>
    public FrameHolder PeekShowable()
    {
        return IsReadIndexShown && ReadableFrames.TryPeek(1, out var frame)
            ? frame
            : ReadableFrames.TryPeek(out frame)
            ? frame
            : throw new InvalidOperationException("Readable frame queue is empty.");
    }

    /// <summary>
    /// Waits for a showable frame to be available and returns it.
    /// Returns null when the queue is closed.
    /// Port of frame_queue_peek_readable.
    /// </summary>
    /// <returns>The frame.</returns>
    public FrameHolder? WaitPeekShowable()
    {
        // wait until we have a readable a new frame
        while (!IsClosed && PendingCount <= 0)
            ChangedEvent.WaitOne(Constants.WaitTimeout, true);

        return !IsClosed
            ? PeekShowable()
            : default;
    }

    /// <summary>
    /// Gets the next+1 available readable frame for showing.
    /// Port of frame_queue_peek_next.
    /// </summary>
    public FrameHolder PeekShowablePlus()
    {
        var index = Convert.ToInt32(IsReadIndexShown) + 1;
        return !ReadableFrames.TryPeek(index, out var frame)
            ? throw new InvalidOperationException("Readable frame queue is empty.")
            : frame;
    }

    /// <summary>
    /// Returns the frame at the current read index
    /// regardless of its show state.
    /// Port of frame_queue_peek_last
    /// </summary>
    public FrameHolder PeekReadable()
    {
        while (!IsClosed && ReadableFrames.IsEmpty)
            ChangedEvent.WaitOne(Constants.WaitTimeout, true);

        return ReadableFrames.TryPeek(out var frame)
            ? frame
            : throw new InvalidOperationException("Readable frame queue is empty.");
    }

    /// <summary>
    /// After using the current readable frame,
    /// advances the current readable index by one
    /// and decrements the count.
    /// Port of frame_queue_next.
    /// </summary>
    public void Dequeue()
    {
        if (KeepLast && !IsReadIndexShown)
        {
            IsReadIndexShown = true;
            return;
        }

        if (ReadableFrames.TryDequeue(out var frame))
        {
            frame.Reset();
            WritableFrames.Enqueue(frame);
        }

        ChangedEvent.Set();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        ReadableFrames.Dispose();
        WritableFrames.Dispose();
        ChangedEvent.Dispose();
    }

    /// <summary>
    /// Implements a thread-safe queue for frames.
    /// </summary>
    private sealed class FrameHolderQueue : IDisposable
    {
        private readonly object SyncLock = new();
        private readonly FrameHolder?[] Frames;
        private readonly int Capacity;

        private int m_HeadSlot;
        private int m_Count;

        public FrameHolderQueue(int capacity, bool fill)
        {
            Frames = new FrameHolder?[capacity];
            Capacity = capacity;

            for (var i = 0; i < capacity; i++)
                Frames[i] = null;

            if (!fill)
                return;

            for (var i = 0; i < Capacity; i++)
                Enqueue(new());
        }

        public int Count { get { lock (SyncLock) return m_Count; } }

        public bool IsEmpty => Count is 0;

        public void Enqueue(FrameHolder item)
        {
            lock (SyncLock)
            {
                if (item is null)
                    throw new ArgumentNullException(nameof(item));

                if (m_Count == Capacity)
                    throw new InvalidOperationException($"Read frame queue is full. Capacity: {Capacity}");

                var tailSlot = (m_HeadSlot + m_Count) % Capacity;
                Frames[tailSlot] = item;
                m_Count++;
            }

        }

        public bool TryDequeue([MaybeNullWhen(false)] out FrameHolder item)
        {
            lock (SyncLock)
            {
                item = Frames[m_HeadSlot];
                if (item is not null)
                {
                    Frames[m_HeadSlot] = null;
                    m_HeadSlot = (m_HeadSlot + 1) % Capacity;
                    m_Count--;
                    return true;
                }

                return false;
            }
        }

        public bool TryPeek([MaybeNullWhen(false)] out FrameHolder item)
        {
            lock (SyncLock)
            {
                item = Frames[m_HeadSlot];
                return item is not null;
            }
        }

        public bool TryPeek(int index, [MaybeNullWhen(false)] out FrameHolder item)
        {
            lock (SyncLock)
            {
                var slot = (m_HeadSlot + index) % Capacity;
                item = Frames[slot];
                return item is not null;
            }
        }

        public void Dispose()
        {
            lock (SyncLock)
            {
                while (!IsEmpty)
                {
                    if (TryDequeue(out var item))
                        item.Dispose();
                }
            }
        }
    }
}
