namespace Unosquare.FFplaySharp.Primitives;

/// <summary>
/// Represents a queue-style data structure used to read and write media frames.
/// </summary>
public sealed class FrameStore : IDisposable, ISerialGroupable
{
    private readonly AutoResetEvent ChangedEvent = new(false);
    private readonly ConcurrentQueue<FrameHolder> WritableFrames = new();
    private readonly ConcurrentQueue<FrameHolder> ReadableFrames = new();
    private readonly PacketStore Packets;

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

        for (var i = 0; i < Capacity; i++)
            WritableFrames.Enqueue(new());
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
    public bool IsClosed => Packets.IsClosed;

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
    public int PendingCount => ReadableFrames.Count - Convert.ToInt32(IsReadIndexShown);

    /// <summary>
    /// Gets the last shown byte position within the stream.
    /// Port of frame_queue_last_pos.
    /// </summary>
    public long ShownBytePosition
    {
        get
        {
            return IsReadIndexShown && ReadableFrames.TryPeek(out var item) && item.GroupIndex == GroupIndex
                ? item.Frame.PacketPosition
                : -1;
        }
    }

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
    public FrameHolder? WaitPeekWriteable()
    {
        // wait until we have space to put a new frame
        // that is, our readable count is less than
        // the capacity of the queue.
        while (!IsClosed && WritableFrames.IsEmpty)
            ChangedEvent.WaitOne(10, true);

        return !IsClosed && WritableFrames.TryPeek(out var frame)
            ? frame
            : default;
    }

    /// <summary>
    /// After obtaining a writable frame by calling
    /// <see cref="WaitPeekWriteable"/> and writing to it,
    /// call this method to commit it and make suck frame readable.
    /// Port of frame_queue_push.
    /// </summary>
    public void Enqueue()
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
        return IsReadIndexShown && ReadableFrames.Count > 1
            ? ReadableFrames.ElementAt(1)
            : ReadableFrames.TryPeek(out var frame)
            ? frame
            : default;
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
            ChangedEvent.WaitOne(10, true);

        return !IsClosed
            ? PeekShowable()
            : default;
    }

    /// <summary>
    /// Gets the next+1 available readable frame for showing.
    /// Port of frame_queue_peek_next.
    /// </summary>
    public FrameHolder PeekShowablePlus() => ReadableFrames.ElementAtOrDefault(Convert.ToInt32(IsReadIndexShown) + 1);

    /// <summary>
    /// Returns the frame at the current read index
    /// regardless of its show state.
    /// Port of frame_queue_peek_last
    /// </summary>
    public FrameHolder PeekReadable()
    {
        while (!IsClosed && ReadableFrames.IsEmpty)
            ChangedEvent.WaitOne(10, true);

        return ReadableFrames.TryPeek(out var frame)
            ? frame
            : default;
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
        while (!ReadableFrames.IsEmpty)
        {
            if (ReadableFrames.TryDequeue(out var frame))
                WritableFrames.Enqueue(frame);
        }

        if (WritableFrames.Count != Capacity)
            throw new InvalidOperationException("Memory leak in frame queue");

        while (!WritableFrames.IsEmpty)
        {
            if (WritableFrames.TryDequeue(out var frame))
                frame?.Dispose();
        }

        ChangedEvent.Dispose();
    }
}
