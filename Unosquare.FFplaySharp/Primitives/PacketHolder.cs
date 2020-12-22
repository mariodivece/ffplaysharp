namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System;

    public unsafe sealed class PacketHolder : ISerialProvider, IDisposable
    {
        static PacketHolder()
        {
            var flushPacket = ffmpeg.av_packet_alloc();
            ffmpeg.av_init_packet(flushPacket);
            flushPacket->data = (byte*)flushPacket;
            FlushPacket = flushPacket;
        }

        public PacketHolder(AVPacket* packetPtr)
        {
            PacketPtr = packetPtr;
            IsFlushPacket = HasFlushData(packetPtr);
        }

        public static AVPacket* FlushPacket { get; }

        public AVPacket* PacketPtr { get; private set; }

        public int Serial { get; set; }

        public bool IsFlushPacket { get; }

        public PacketHolder Next { get; set; }

        public static bool HasFlushData(AVPacket* packet)
            => packet != null && packet->data == FlushPacket->data;

        public void Dispose()
        {
            var packetPtr = PacketPtr;

            if (packetPtr != null)
                ffmpeg.av_packet_free(&packetPtr);

            PacketPtr = null;
        }
    }
}
