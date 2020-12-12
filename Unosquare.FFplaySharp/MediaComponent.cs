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

        public abstract AVMediaType MediaType { get; }

        public bool IsAudio => MediaType == AVMediaType.AVMEDIA_TYPE_AUDIO;
        public bool IsVideo => MediaType == AVMediaType.AVMEDIA_TYPE_VIDEO;

        public bool IsSubtitle => MediaType == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

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

        public virtual void Close()
        {
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext->nb_streams)
                return;

            Decoder?.Abort();
            Decoder?.Dispose();
            Container.InputContext->streams[StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
            Stream = null;
            StreamIndex = -1;
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

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_VIDEO;
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

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_AUDIO;

        public override void Close()
        {
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext->nb_streams)
                return;

            Decoder?.Abort();
            Container.Renderer.CloseAudio();
            Decoder?.Dispose();

            var contextPointer = ConvertContext;
            ffmpeg.swr_free(&contextPointer);
            ConvertContext = null;

            if (Container.audio_buf1 != null)
                ffmpeg.av_free(Container.audio_buf1);

            Container.audio_buf1 = null;
            Container.audio_buf1_size = 0;
            Container.audio_buf = null;

            Container.InputContext->streams[StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
            Stream = null;
            StreamIndex = -1;
        }
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

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_SUBTITLE;
    }
}
