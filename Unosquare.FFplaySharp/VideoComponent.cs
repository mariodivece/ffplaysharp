namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;

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

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.VIDEO_PICTURE_QUEUE_SIZE, true);
    }

}
