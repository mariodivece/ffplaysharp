namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;
    using System.Runtime.InteropServices;

    public unsafe sealed class Packet : UnmanagedReference<AVPacket>, ISerialGroupable
    {
        public Packet(AVPacket* pointer)
            : base()
        {
            Update(pointer);
        }

        public Packet()
            : this(ffmpeg.av_packet_alloc())
        {
            // placeholder
        }

        public static int StructureSize { get; } = Marshal.SizeOf<AVPacket>();

        public Packet Next { get; set; }

        public int GroupIndex { get; set; }

        public bool IsFlushPacket { get; private set; }

        public int StreamIndex
        {
            get => Pointer->stream_index;
            set => Pointer->stream_index = value;
        }

        public int Size
        {
            get => Pointer->size;
            set => Pointer->size = value;
        }

        public long DurationUnits
        {
            get => Pointer->duration;
            set => Pointer->duration = value;
        }

        public byte* Data
        {
            get => Pointer->data;
            set => Pointer->data = value;
        }

        public long Pts => Pointer->pts.IsValidPts()
            ? Pointer->pts
            : Pointer->dts;

        public static Packet Clone(AVPacket* packet)
        {
            var copy = ffmpeg.av_packet_clone(packet);
            return new Packet(copy);
        }
        public Packet Clone() =>
            new(ffmpeg.av_packet_clone(Pointer));

        public static Packet CreateFlushPacket()
        {
            var packet = new Packet()
            {
                Size = 0,
                IsFlushPacket = true
            };

            packet.Data = (byte*)packet.Pointer;
            return packet;
        }

        public static Packet CreateNullPacket(int streamIndex) => new()
        {
            Data = null,
            Size = 0,
            StreamIndex = streamIndex
        };

        protected override void ReleaseInternal(AVPacket* pointer) =>
            ffmpeg.av_packet_free(&pointer);
    }
}
