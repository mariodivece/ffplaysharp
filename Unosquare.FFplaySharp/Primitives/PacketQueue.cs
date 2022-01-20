#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
namespace Unosquare.FFplaySharp.Primitives;

/// <summary>
/// Represents a queue of packets that belong to a specific component.
/// </summary>
public sealed class PacketQueue : ISerialGroupable, IDisposable
{
    private readonly ConcurrentQueue<FFPacket> queue = new();
    private readonly AutoResetEvent IsAvailableEvent = new(false);
    private bool isDisposed;
    private long m_IsClosed = 1; // starts in a blocked state
    private long m_ByteSize;
    private long m_DurationUnits;
    private long m_GroupIndex;

    /// <summary>
    /// Creates a new instance of the <see cref="PacketQueue"/> class.
    /// </summary>
    /// <param name="component">The associated component.</param>
    public PacketQueue(MediaComponent component)
    {
        Component = component;
    }

    /// <summary>
    /// Gets the associated component.
    /// </summary>
    public MediaComponent Component { get; }

    /// <summary>
    /// Gets the numer of packets in the queue.
    /// </summary>
    public int Count => queue.Count;

    /// <summary>
    /// Gets the total size in bytes of the packets
    /// and their content buffers.
    /// </summary>
    public int ByteSize
    {
        get => (int)Interlocked.Read(ref m_ByteSize);
        private set => Interlocked.Exchange(ref m_ByteSize, value);
    }

    /// <summary>
    /// Gets the packet queue duration in stream time base units.
    /// </summary>
    public long DurationUnits
    {
        get => Interlocked.Read(ref m_DurationUnits);
        private set => Interlocked.Exchange(ref m_DurationUnits, value);
    }

    /// <summary>
    /// Gets the group (serial) the packet queue is currently on.
    /// </summary>
    public int GroupIndex
    {
        get => (int)Interlocked.Read(ref m_GroupIndex);
        private set => Interlocked.Exchange(ref m_GroupIndex, value);
    }

    /// <summary>
    /// Gets whether the packet queue is closed.
    /// When closed, packets cannot be queued or dequeued.
    /// </summary>
    public bool IsClosed
    {
        get => Interlocked.Read(ref m_IsClosed) != 0;
        private set => Interlocked.Exchange(ref m_IsClosed, value ? 1 : 0);
    }

    /// <summary>
    /// Opens the queue for enqueueing and dequeueing.
    /// </summary>
    public void Open()
    {
        if (isDisposed)
            throw new ObjectDisposedException(nameof(PacketQueue));

        IsClosed = false;
        EnqueueFlush();
    }

    /// <summary>
    /// Equeues a data packet. Will return false and release the passed packet
    /// if the queue is closed.
    /// </summary>
    /// <param name="packet">The packet to enqueue.</param>
    /// <returns>True when the operation succeeds. False otherwise.</returns>
    public bool Enqueue(FFPacket packet)
    {
        if (IsClosed)
        {
            packet?.Release();
            return false;
        }

        if (packet.IsNull())
            throw new ArgumentNullException(nameof(packet));

        packet.GroupIndex = packet.IsFlushPacket
            ? ++GroupIndex
            : GroupIndex;

        ByteSize += packet.Size + FFPacket.StructureSize;
        DurationUnits += packet.DurationUnits;
        queue.Enqueue(packet);
        IsAvailableEvent.Set();
        return true;
    }

    /// <summary>
    /// Enqueues a codec flush packet to signal the start of a packet sequence.
    /// </summary>
    /// <returns>The result of the <see cref="Enqueue(FFPacket)"/> operation.</returns>
    public bool EnqueueFlush() =>
        Enqueue(FFPacket.CreateFlushPacket());

    /// <summary>
    /// Enqueues a null packet to signal the end of a packet sequence.
    /// </summary>
    /// <returns>The result of the <see cref="Enqueue(FFPacket)"/> operation.</returns>
    public bool EnqueueNull() =>
        Enqueue(FFPacket.CreateNullPacket(Component.StreamIndex));

    /// <summary>
    /// Tries to obtain the next available packet in the queue.
    /// Will return false if the queue is closed.
    /// </summary>
    /// <param name="blockWait">When true, waits for a newly available packet.</param>
    /// <returns>True when the operation succeeds. False otherwise.</returns>
    public bool TryDequeue(bool blockWait, [MaybeNullWhen(false)] out FFPacket? packet)
    {
        packet = default;
        while (true)
        {
            if (IsClosed)
                return default;

            if (queue.TryDequeue(out packet) && packet.IsNotNull())
            {
                ByteSize -= packet.Size + FFPacket.StructureSize;
                DurationUnits -= packet.DurationUnits;
                return true;
            }

            if (!blockWait)
                return default;

            if (!IsAvailableEvent.WaitOne())
                continue;
        }
    }

    /// <summary>
    /// Clears all the packets in the queues and disposes them.
    /// </summary>
    public void Clear()
    {
        while (!queue.IsEmpty)
        {
            if (queue.TryDequeue(out var packet) && packet.IsNotNull())
                packet.Release();
        }

        ByteSize = 0;
        DurationUnits = 0;
    }

    /// <summary>
    /// Closes the packet queue preventing more packets from being queued.
    /// </summary>
    public void Close()
    {
        IsClosed = true;
        IsAvailableEvent.Set();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        Close();
        Clear();
        IsAvailableEvent.Set();
        IsAvailableEvent.Dispose();
    }
}
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix