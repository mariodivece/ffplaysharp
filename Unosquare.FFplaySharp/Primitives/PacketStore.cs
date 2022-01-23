namespace Unosquare.FFplaySharp.Primitives;

/// <summary>
/// Represents a queue-style data structure that stores
/// stream packets that belong to a specific component.
/// </summary>
public sealed class PacketStore : ISerialGroupable, IDisposable
{
    private readonly object SyncLock = new();
    private readonly Queue<FFPacket> Packets = new();
    private readonly AutoResetEvent IsAvailableEvent = new(false);
    private bool isDisposed;
    private long m_IsClosed = 1; // starts in a blocked state

    private int m_ByteSize;
    private long m_DurationUnits;
    private int m_GroupIndex;

    /// <summary>
    /// Creates a new instance of the <see cref="PacketStore"/> class.
    /// </summary>
    /// <param name="component">The associated component.</param>
    public PacketStore(MediaComponent component)
    {
        Component = component;
    }

    /// <summary>
    /// Gets the associated component.
    /// </summary>
    public MediaComponent Component { get; }

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
    /// Gets the numer of packets in the queue.
    /// </summary>
    public int Count
    {
        get { lock (SyncLock) return Packets.Count; }
    }

    /// <summary>
    /// Gets the total size in bytes of the packets
    /// and their content buffers.
    /// </summary>
    public int ByteSize
    {
        get { lock (SyncLock) return m_ByteSize; }
    }

    /// <summary>
    /// Gets the packet queue duration in stream time base units.
    /// </summary>
    public long DurationUnits
    {
        get { lock (SyncLock) return m_DurationUnits; }
    }

    /// <summary>
    /// Gets the group (serial) the packet queue is currently on.
    /// </summary>
    public int GroupIndex
    {
        get { lock (SyncLock) return m_GroupIndex; }
    }

    /// <summary>
    /// Opens the queue for enqueueing and dequeueing.
    /// </summary>
    public void Open()
    {
        if (isDisposed)
            throw new ObjectDisposedException(nameof(PacketStore));

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

        if (packet is null)
            throw new ArgumentNullException(nameof(packet));

        lock (SyncLock)
        {
            packet.GroupIndex = packet.IsFlushPacket
                ? ++m_GroupIndex
                : m_GroupIndex;

            m_ByteSize += packet.Size + FFPacket.StructureSize;
            m_DurationUnits += packet.DurationUnits;
            Packets.Enqueue(packet);
        }

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

            lock (SyncLock)
            {
                if (Packets.TryDequeue(out packet) && packet is not null)
                {
                    m_ByteSize -= packet.Size + FFPacket.StructureSize;
                    m_DurationUnits -= packet.DurationUnits;
                    return true;
                }
            }

            if (!blockWait)
                return default;

            IsAvailableEvent.WaitOne(Constants.WaitTimeout, true);
        }
    }

    /// <summary>
    /// Clears all the packets in the queues and disposes them.
    /// </summary>
    public void Clear()
    {
        lock (SyncLock)
        {
            while (Packets.Count != 0)
            {
                if (Packets.TryDequeue(out var packet) && packet.IsNotNull())
                    packet.Release();
            }

            m_ByteSize = 0;
            m_DurationUnits = 0;
        }
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
