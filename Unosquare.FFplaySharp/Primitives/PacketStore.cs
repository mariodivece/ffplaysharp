using System.Threading.Channels;

namespace Unosquare.FFplaySharp.Primitives;

/// <summary>
/// Represents a queue-style data structure that stores
/// stream packets that belong to a specific component.
/// This class is backed by a <see cref="Channel{T}"/> and it is
/// thread-safe.
/// </summary>
public sealed class PacketStore :
    ISerialGroupable,
    IDisposable
{
    private readonly object SyncRoot = new();
    private readonly Channel<FFPacket> PacketChannel;

    private long m_IsDisposed;
    private long m_IsClosed;

    private int m_ByteSize;
    private long m_DurationUnits;
    private int m_GroupIndex;

    /// <summary>
    /// Creates a new instance of the <see cref="PacketStore"/> class.
    /// </summary>
    public PacketStore()
    {
        // starts in a blocked state
        IsClosed = true;
        PacketChannel = Channel.CreateUnbounded<FFPacket>(new()
        {
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = true,
        });
    }

    /// <summary>
    /// Gets whether the packet queue is closed.
    /// When closed, packets cannot be queued or dequeued.
    /// </summary>
    public bool IsClosed
    {
        get => Interlocked.Read(ref m_IsClosed) > 0;
        private set => Interlocked.Exchange(ref m_IsClosed, value ? 1 : 0);
    }

    /// <summary>
    /// Gets the numer of packets in the queue.
    /// </summary>
    public int Count => PacketChannel.Reader.CanCount ? PacketChannel.Reader.Count : 0;

    /// <summary>
    /// Gets the total size in bytes of the packets
    /// and their content buffers.
    /// </summary>
    public int ByteSize
    {
        get
        {
            lock (SyncRoot)
                return m_ByteSize;
        }
    }

    /// <summary>
    /// Gets the packet queue duration in stream time base units.
    /// </summary>
    public long DurationUnits
    {
        get
        {
            lock (SyncRoot)
                return m_DurationUnits;
        }
    }

    /// <summary>
    /// Gets the group (serial) the packet queue is currently on.
    /// </summary>
    public int GroupIndex
    {
        get
        {
            lock (SyncRoot)
                return m_GroupIndex;
        }
    }

    /// <summary>
    /// Opens the queue for enqueueing and dequeueing.
    /// Automatically enqueues a special flush packet.
    /// This method has to be called in order to start enqueuing packets.
    /// </summary>
    public void Open()
    {
        ObjectDisposedException.ThrowIf(
            Interlocked.Read(ref m_IsDisposed) > 0, this);

        IsClosed = false;
        EnqueueFlush();
    }

    /// <summary>
    /// Enqueues a null packet to signal the end of a packet sequence.
    /// </summary>
    /// <param name="streamIndex">The stream index to which this packet belongs.</param>
    /// <returns>The result of the <see cref="Enqueue(FFPacket)"/> operation.</returns>
    public bool EnqueueNull(int streamIndex) =>
        Enqueue(FFPacket.CreateNullPacket(streamIndex));

    /// <summary>
    /// Enqueues a codec flush packet to signal the start of a packet sequence.
    /// </summary>
    /// <returns>The result of the <see cref="Enqueue(FFPacket)"/> operation.</returns>
    public bool EnqueueFlush() =>
        Enqueue(FFPacket.CreateFlushPacket());

    /// <summary>
    /// Closes the packet queue preventing more packets from being queued.
    /// This method can only be called once.
    /// </summary>
    public void Close()
    {
        if (Interlocked.Increment(ref m_IsClosed) > 1)
            return;

        _ = PacketChannel.Writer.TryComplete();
    }

    /// <summary>
    /// Equeues a data packet. Will return false and release the passed packet
    /// if the queue is closed.
    /// </summary>
    /// <remarks>
    /// ffplay.c packet_queue_put_private
    /// </remarks>
    /// <param name="packet">The packet to enqueue.</param>
    /// <returns>True when the operation succeeds. False otherwise.</returns>
    public bool Enqueue(FFPacket packet)
    {
        ArgumentNullException.ThrowIfNull((object?)packet);

        if (IsClosed)
        {
            packet.Dispose();
            return false;
        }

        lock (SyncRoot)
        {
            if (!PacketChannel.Writer.TryWrite(packet))
            {
                packet.Dispose();
                return false;
            }

            if (packet.IsFlushPacket)
                m_GroupIndex++;

            packet.GroupIndex = m_GroupIndex;

            m_ByteSize += packet.DataSize + packet.StructureSize;
            m_DurationUnits += packet.DurationUnits;
        }

        return true;
    }

    /// <summary>
    /// Tries to obtain the next available packet in the queue.
    /// Will return false if the queue is closed.
    /// </summary>
    /// <remarks>
    /// ffplay.c packet_queue_get
    /// </remarks>
    /// <param name="blockWait">When true, blocks and waits for a newly available packet.</param>
    /// <param name="packet">The dequeued packet. May contain null if no packet was dequeued.</param>
    /// <returns>True when the operation succeeds. False otherwise.</returns>
    public bool TryDequeue(bool blockWait, [MaybeNullWhen(false)] out FFPacket? packet)
    {
        packet = default;

        if (IsClosed)
            return default;

        if (blockWait)
        {
            var waitTask = PacketChannel.Reader.WaitToReadAsync(CancellationToken.None);
            if (!waitTask.IsCompleted)
                _ = waitTask.AsTask().GetAwaiter().GetResult();
        }

        lock (SyncRoot)
        {
            if (PacketChannel.Reader.TryRead(out packet) && packet is not null)
            {
                m_ByteSize -= (packet.DataSize + packet.StructureSize);
                m_DurationUnits -= packet.DurationUnits;
                return true;
            }
        }

        return default;
    }

    /// <summary>
    /// Clears all the packets in the queues and disposes them.
    /// </summary>
    public void Clear()
    {
        while (Count > 0)
        {
            lock (SyncRoot)
            {
                FFPacket? packet = null;
                try
                {
                    if (!PacketChannel.Reader.TryRead(out packet) || packet is null)
                        continue;

                    m_ByteSize -= (packet.DataSize + packet.StructureSize);
                    m_DurationUnits -= packet.DurationUnits;
                }
                finally
                {
                    packet?.Dispose();
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Increment(ref m_IsDisposed) > 1)
            return;

        Close();
        Clear();
    }
}

