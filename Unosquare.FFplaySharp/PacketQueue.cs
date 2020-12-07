using FFmpeg.AutoGen;
using System;
using System.Threading;

namespace Unosquare.FFplaySharp
{
    public unsafe sealed class PacketQueue : ISerialProvider, IDisposable
    {
        private readonly object SyncLock = new();
        private readonly ManualResetEventSlim IsAvailableEvent = new(false);
        private bool m_IsClosed = true; // starts in a blocked state
        private PacketHolder First;
        private PacketHolder Last;

        public int Count { get; private set; }

        public int Size { get; private set; }

        public long Duration { get; private set; }

        public int Serial { get; private set; }

        public bool IsClosed
        {
            get
            {
                lock (SyncLock)
                    return m_IsClosed;
            }
        }

        public bool PutFlush() => Put(PacketHolder.FlushPacket);

        public bool PutNull(int streamIndex)
        {
            var packet = ffmpeg.av_packet_alloc();
            packet->data = null;
            packet->size = 0;
            packet->stream_index = streamIndex;
            return Put(packet);
        }

        public bool Put(AVPacket* packetPtr)
        {
            var result = true;
            lock (SyncLock)
            {
                var pkt1 = new PacketHolder(packetPtr) { Next = null };

                if (m_IsClosed)
                {
                    result = false;
                }
                else
                {
                    if (pkt1.IsFlushPacket)
                        Serial++;

                    pkt1.Serial = Serial;

                    if (Last == null)
                        First = pkt1;
                    else
                        Last.Next = pkt1;

                    Last = pkt1;
                    Count++;
                    Size += pkt1.PacketPtr->size + sizeof(IntPtr);
                    Duration += pkt1.PacketPtr->duration;
                }

                if (!pkt1.IsFlushPacket && !result)
                    pkt1.Dispose();

                return result;
            }
        }

        public void Clear()
        {
            lock (SyncLock)
            {
                for (var pkt = First; pkt != null; pkt = pkt?.Next)
                    pkt?.Dispose();

                Last = null;
                First = null;
                Count = 0;
                Size = 0;
                Duration = 0;
            }
        }

        public void Dispose()
        {
            lock (SyncLock)
            {
                Close();
                Clear();
                IsAvailableEvent.Dispose();
            }

        }

        public void Close()
        {
            lock (SyncLock)
            {
                m_IsClosed = true;
            }
        }

        public void Open()
        {
            lock (SyncLock)
            {
                m_IsClosed = false;
                Put(PacketHolder.FlushPacket);
            }
        }

        public PacketHolder Get(bool block)
        {
            while (true)
            {
                lock (SyncLock)
                {
                    if (m_IsClosed)
                        return null;

                    var item = First;
                    if (item != null)
                    {
                        First = item.Next;
                        if (First == null)
                            Last = null;

                        Count--;
                        Size -= (item.PacketPtr->size + sizeof(IntPtr));
                        Duration -= (item.PacketPtr->duration);
                        return item;
                    }
                    else if (!block)
                    {
                        return null;
                    }
                    else
                    {
                        IsAvailableEvent.Wait(10);
                    }
                }
            }
        }
    }
}
