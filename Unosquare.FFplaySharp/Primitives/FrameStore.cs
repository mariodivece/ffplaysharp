namespace Unosquare.FFplaySharp.Primitives;

/// <summary>
/// Represents a queue-style data structure used to read and write media frames.
/// </summary>
public sealed class FrameStore : IDisposable, ISerialGroupable
{
    private readonly AutoResetEvent ChangedEvent = new(false);
    private readonly FrameHolder[] Frames;
    private readonly PacketStore Packets;

    private long m_IsReadIndexShown;
    private long m_ReadIndex;
    private long m_WriteIndex;
    private long m_Count;

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

        Frames = new FrameHolder[Capacity];
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new FrameHolder();
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
    /// Gets a value indicating if the frame at the current
    /// <see cref="ReadIndex"/> has been shown by the <see cref="IPresenter"/>
    /// </summary>
    public bool IsReadIndexShown
    {
        get => Interlocked.Read(ref m_IsReadIndexShown) != 0;
        private set => Interlocked.Exchange(ref m_IsReadIndexShown, value ? 1 : 0);
    }

    /// <summary>
    /// Gets the current frame index in the queue that can be read,
    /// regardless of whether or not the frame has been shown.
    /// </summary>
    public int ReadIndex
    {
        get => (int)Interlocked.Read(ref m_ReadIndex);
        private set => Interlocked.Exchange(ref m_ReadIndex, value);
    }

    /// <summary>
    /// Gets the current frame index in the queue that can be written to.
    /// Port of windex.
    /// </summary>
    public int WriteIndex
    {
        get => (int)Interlocked.Read(ref m_WriteIndex);
        private set => Interlocked.Exchange(ref m_WriteIndex, value);
    }

    /// <summary>
    /// Gets the current number of frames available for reading off
    /// this frame queue.
    /// </summary>
    public int Count
    {
        get => (int)Interlocked.Read(ref m_Count);
        private set => Interlocked.Exchange(ref m_Count, value);
    }

    /// <summary>
    /// Gets the number the number of undisplayed frames in the queue.
    /// Port of frame_queue_nb_remaining.
    /// </summary>
    public int PendingCount => Count - (IsReadIndexShown ? 1 : 0);

    /// <summary>
    /// Gets the last shown byte position within the stream.
    /// Port of frame_queue_last_pos.
    /// </summary>
    public long ShownBytePosition
    {
        get
        {
            var currentFrame = Frames[ReadIndex];
            return IsReadIndexShown && currentFrame.GroupIndex == GroupIndex
                ? currentFrame.Frame.PacketPosition
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
    public FrameHolder? PeekWriteable()
    {
        // wait until we have space to put a new frame
        // that is, our readable count is less than
        // the capacity of the queue.
        while (Count >= Capacity && !IsClosed)
            ChangedEvent.WaitOne(10, true);

        return !IsClosed
            ? Frames[WriteIndex]
            : default;
    }

    /// <summary>
    /// Gets the next available readable frame for showing.
    /// Port of frame_queue_peek.
    /// </summary>
    public FrameHolder PeekShowable() =>
        Frames[(ReadIndex + (IsReadIndexShown ? 1 : 0)) % Capacity];

    /// <summary>
    /// Waits for a showable frame to be available and returns it.
    /// Returns null when the queue is closed.
    /// Port of frame_queue_peek_readable.
    /// </summary>
    /// <returns>The frame.</returns>
    public FrameHolder? WaitPeekShowable()
    {
        // wait until we have a readable a new frame
        while (Count - (IsReadIndexShown ? 1 : 0) <= 0 && !IsClosed)
            ChangedEvent.WaitOne(10, true);

        return !IsClosed
            ? PeekShowable()
            : default;
    }

    /// <summary>
    /// Gets the next+1 available readable frame for showing.
    /// Port of frame_queue_peek_next.
    /// </summary>
    public FrameHolder PeekShowablePlus() =>
        Frames[(ReadIndex + (IsReadIndexShown ? 1 : 0) + 1) % Capacity];

    /// <summary>
    /// Returns the frame at the current read index
    /// regardless of its show state.
    /// Port of frame_queue_peek_last
    /// </summary>
    public FrameHolder PeekReadable() =>
        Frames[ReadIndex];

    /// <summary>
    /// After obtaining a writable frame by calling
    /// <see cref="PeekWriteable"/> and writing to it,
    /// call this method to commit it and make suck frame readable.
    /// Port of frame_queue_push.
    /// </summary>
    public void Enqueue()
    {
        if (++WriteIndex >= Capacity)
            WriteIndex = 0;

        Count++;
        ChangedEvent.Set();
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

        Frames[ReadIndex].Reset();
        if (++ReadIndex >= Capacity)
            ReadIndex = 0;

        Count--;
        ChangedEvent.Set();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // port of frame_queue_destory
        for (var i = 0; i < Frames.Length; i++)
        {
            Frames[i].Dispose();
            Frames[i] = default;
        }

        ChangedEvent.Dispose();
    }
}
