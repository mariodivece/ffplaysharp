namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System;
    using System.Runtime.InteropServices;

    public unsafe sealed class PacketHolder : ISerialProvider, IDisposable
    {
        public PacketHolder(AVPacket* packetPtr)
        {
            PacketPtr = packetPtr;
            IsFlushPacket = HasFlushData(packetPtr);
        }

        public static AVPacket* CreateFlushPacket()
        {
            var flushPacket = ffmpeg.av_packet_alloc();
            ffmpeg.av_init_packet(flushPacket);
            flushPacket->data = (byte*)flushPacket;
            return flushPacket;
        }

        public static int PacketStructureSize { get; } = Marshal.SizeOf<AVPacket>();

        public AVPacket* PacketPtr { get; private set; }

        public int Serial { get; set; }

        public bool IsFlushPacket { get; }

        public PacketHolder Next { get; set; }

        public static bool HasFlushData(AVPacket* packet)
            => packet != null && packet->data == (byte*)packet;

        public void Dispose()
        {
            var packetPtr = PacketPtr;

            if (packetPtr != null)
                ffmpeg.av_packet_free(&packetPtr);

            PacketPtr = null;
        }
    }
}
