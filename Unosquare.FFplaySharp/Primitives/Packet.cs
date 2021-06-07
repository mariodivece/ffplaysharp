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

        public Packet()
            : this(ffmpeg.av_packet_alloc())
        {
            // placeholder
        }

        public static int StructureSize { get; } = Marshal.SizeOf<AVPacket>();

        public int Serial { get; set; }

        public bool IsFlushPacket { get; }

        public int StreamIndex => Pointer->stream_index;

        public long Pts => Pointer->pts.IsValidPts()
            ? Pointer->pts
            : Pointer->dts;

        public Packet Next { get; set; }

        public Packet Clone() =>
            new(ffmpeg.av_packet_clone(Pointer));

        public static Packet Clone(AVPacket* packet)
        {
            var copy = ffmpeg.av_packet_clone(packet);
            return new Packet(copy);
        }

        private static bool HasFlushData(AVPacket* packet)
            => packet != null && packet->data == (byte*)packet;

        public static Packet CreateFlushPacket()
        {
            var packet = ffmpeg.av_packet_alloc();
            packet->data = (byte*)packet;
            return new(packet);
        }

        public static Packet CreateNullPacket(int streamIndex)
        {
            var packet = ffmpeg.av_packet_alloc();
            packet->data = null;
            packet->size = 0;
            packet->stream_index = streamIndex;

            return new(packet);
        }

        protected override void ReleaseInternal(AVPacket* pointer) =>
            ffmpeg.av_packet_free(&pointer);
    }
}
