namespace Unosquare.FFplaySharp.Primitives;

public sealed class PacketQueue : ISerialGroupable, IDisposable
{
    private readonly object SyncLock = new();
    private readonly ManualResetEventSlim IsAvailableEvent = new(false);
    private long m_IsClosed = 1; // starts in a blocked state
    private FFPacket? First;
    private FFPacket? Last;

    private int m_Count;
    private int m_ByteSize;
    private long m_DurationUnits;
    private int m_GroupIndex;

    public PacketQueue(MediaComponent component)
    {
        Component = component;

    }

    public MediaComponent Component { get; }

    public int Count
    {
        get { lock (SyncLock) return m_Count; }
        private set { lock (SyncLock) m_Count = value; }
    }

    public int ByteSize
    {
        get { lock (SyncLock) return m_ByteSize; }
        private set { lock (SyncLock) m_ByteSize = value; }
    }

    /// <summary>
    /// Gets the packet queue duration in stream time base units.
    /// </summary>
    public long DurationUnits
    {
        get { lock (SyncLock) return m_DurationUnits; }
        private set { lock (SyncLock) m_DurationUnits = value; }
    }

    /// <summary>
    /// The serial is the group (serial) the packet queue belongs to.
    /// </summary>
    public int GroupIndex
    {
        get { lock (SyncLock) return m_GroupIndex; }
        private set { lock (SyncLock) m_GroupIndex = value; }
    }

    public bool IsClosed
    {
        get => Interlocked.Read(ref m_IsClosed) != 0;
        set => Interlocked.Exchange(ref m_IsClosed, value ? 1 : 0);
    }

    public void Open()
    {
        IsClosed = false;
        EnqueueFlush();
    }

    public bool Enqueue(FFPacket packet)
    {
        if (packet.IsNull())
            throw new ArgumentNullException(nameof(packet));

        var result = true;
        lock (SyncLock)
        {
            packet.Next = default;

            if (IsClosed)
            {
                result = false;
            }
            else
            {
                if (packet.IsFlushPacket)
                    GroupIndex++;

                packet.GroupIndex = GroupIndex;

                if (Last.IsNull())
                    First = packet;
                else
                    Last!.Next = packet;

                Last = packet;
                Count++;
                ByteSize += packet.Size + FFPacket.StructureSize;
                DurationUnits += packet.DurationUnits;
                IsAvailableEvent.Set();
            }

            if (!result)
                packet.Release();
        }

        return result;
    }

    public bool EnqueueFlush() =>
        Enqueue(FFPacket.CreateFlushPacket());

    public bool EnqueueNull() =>
        Enqueue(FFPacket.CreateNullPacket(Component.StreamIndex));

    public FFPacket? Dequeue(bool blockWait)
    {
        while (true)
        {
            if (IsClosed)
                return default;

            lock (SyncLock)
            {
                var item = First;
                if (item.IsNotNull())
                {
                    First = item!.Next;
                    if (First.IsNull())
                        Last = default;

                    Count--;
                    ByteSize -= item.Size + FFPacket.StructureSize;
                    DurationUnits -= item.DurationUnits;
                    return item;
                }
            }

            if (!blockWait)
                return default;

            if (!IsAvailableEvent.Wait(Constants.EventWaitTime))
                continue;

            IsAvailableEvent.Reset();
        }
    }

    public void Clear()
    {
        lock (SyncLock)
        {
            for (var currentPacket = First; currentPacket.IsNotNull(); currentPacket = currentPacket?.Next)
                currentPacket?.Release();

            Last = default;
            First = default;
            Count = 0;
            ByteSize = 0;
            DurationUnits = 0;
        }
    }

    public void Close()
    {
        IsClosed = true;
        IsAvailableEvent.Set();
    }

    public void Dispose()
    {
        lock (SyncLock)
        {
            Close();
            Clear();
        }

        IsAvailableEvent.Set();
        IsAvailableEvent.Dispose();
    }
}
