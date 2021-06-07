namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System.Runtime.InteropServices;

    public unsafe sealed class Packet : UnmanagedReference<AVPacket>, ISerialProvider
    {
        public Packet(AVPacket* pointer)
            : base()
        {
            Update(pointer);
            IsFlushPacket = HasFlushData(pointer);
        }

        public static int StructureSize { get; } = Marshal.SizeOf<AVPacket>();

        public int Serial { get; set; }

        public bool IsFlushPacket { get; }

        public Packet Next { get; set; }

        public Packet Clone() =>
            new(ffmpeg.av_packet_clone(Pointer));

        private static bool HasFlushData(AVPacket* packet)
            => packet != null && packet->data == (byte*)packet;

        public static AVPacket* CreateFlushPacket()
        {
            var flushPacket = ffmpeg.av_packet_alloc();
            flushPacket->data = (byte*)flushPacket;
            return flushPacket;
        }

        public static AVPacket* CreateNullPacket(int streamIndex)
        {
            var packet = ffmpeg.av_packet_alloc();
            packet->data = null;
            packet->size = 0;
            packet->stream_index = streamIndex;

            return packet;
        }

        protected override void ReleaseInternal(AVPacket* pointer) =>
            ffmpeg.av_packet_free(&pointer);
    }
}
