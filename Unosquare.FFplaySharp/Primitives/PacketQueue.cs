namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System;
    using System.Threading;
    using Unosquare.FFplaySharp.Components;

    public unsafe sealed class PacketQueue : ISerialProvider, IDisposable
    {
        private readonly object SyncLock = new();
        private readonly AutoResetEvent IsAvailableEvent = new(false);
        private bool m_IsClosed = true; // starts in a blocked state
        private Packet First;
        private Packet Last;

        private int m_Count;
        private int m_ByteSize;
        private long m_DurationUnits;
        private int m_Serial;

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
        public int Serial
        {
            get { lock (SyncLock) return m_Serial; }
            private set { lock (SyncLock) m_Serial = value; }
        }

        public bool IsClosed
        {
            get { lock (SyncLock) return m_IsClosed; }
        }

        public void Open()
        {
            lock (SyncLock)
            {
                m_IsClosed = false;
                EnqueueFlush();
            }
        }

        public bool Enqueue(Packet packet)
        {
            var result = true;
            lock (SyncLock)
            {
                packet.Next = null;

                if (m_IsClosed)
                {
                    result = false;
                }
                else
                {
                    if (packet.IsFlushPacket)
                        Serial++;

                    packet.Serial = Serial;

                    if (Last == null)
                        First = packet;
                    else
                        Last.Next = packet;

                    Last = packet;
                    Count++;
                    ByteSize += packet.Value.size + Packet.StructureSize;
                    DurationUnits += packet.Value.duration;
                    IsAvailableEvent.Set();
                }

                if (!result)
                    packet.Release();
            }

            return result;
        }

        public bool EnqueueFlush() =>
            Enqueue(Packet.CreateFlushPacket());

        public bool EnqueueNull() =>
            Enqueue(Packet.CreateNullPacket(Component.StreamIndex));

        public Packet Dequeue(bool blockWait)
        {
            while (true)
            {
                if (IsClosed)
                    return null;

                lock (SyncLock)
                {
                    var item = First;
                    if (item != null)
                    {
                        First = item.Next;
                        if (First == null)
                            Last = null;

                        Count--;
                        ByteSize -= item.Value.size + Packet.StructureSize;
                        DurationUnits -= item.Value.duration;
                        return item;
                    }
                }

                if (!blockWait)
                    return null;
                else
                    IsAvailableEvent.WaitOne();
            }
        }

        public void Clear()
        {
            lock (SyncLock)
            {
                for (var currentPacket = First; currentPacket != null; currentPacket = currentPacket?.Next)
                    currentPacket?.Release();

                Last = null;
                First = null;
                Count = 0;
                ByteSize = 0;
                DurationUnits = 0;
            }
        }

        public void Close()
        {
            lock (SyncLock)
                m_IsClosed = true;

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
}
