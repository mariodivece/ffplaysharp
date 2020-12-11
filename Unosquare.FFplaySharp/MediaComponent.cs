namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;

    public abstract unsafe class MediaComponent
    {
        protected MediaComponent(MediaContainer container)
        {
            Container = container;
            Packets = new(this);
        }

        public MediaContainer Container { get; }

        public PacketQueue Packets { get; }

        public FrameQueue Frames;

        public MediaDecoder Decoder { get; set; }

        public AVStream* Stream;

        public int StreamIndex;

        public int LastStreamIndex;

        public bool HasEnoughPackets
        {
            get
            {
                return StreamIndex < 0 ||
                   Packets.IsClosed ||
                   (Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0 ||
                   Packets.Count > Constants.MIN_FRAMES && (Packets.Duration == 0 ||
                   ffmpeg.av_q2d(Stream->time_base) * Packets.Duration > 1.0);
            }
        }
    }

    public abstract unsafe class FilteringMediaComponent : MediaComponent
    {
        protected FilteringMediaComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public AVFilterContext* InputFilter;
        public AVFilterContext* OutputFilter;
    }

    public unsafe sealed class VideoComponent : FilteringMediaComponent
    {
        public VideoComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwsContext* ConvertContext;

        public new VideoDecoder Decoder { get; set; }
    }

    public unsafe sealed class AudioComponent : FilteringMediaComponent
    {
        public AudioComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwrContext* ConvertContext;

        public AudioParams SourceSpec = new();
        public AudioParams FilterSpec = new();
        public AudioParams TargetSpec = new();

        public new AudioDecoder Decoder { get; set; }
    }

    public unsafe sealed class SubtitleComponent : MediaComponent
    {
        public SubtitleComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwsContext* ConvertContext;

        public new SubtitleDecoder Decoder { get; set; }
    }
}
