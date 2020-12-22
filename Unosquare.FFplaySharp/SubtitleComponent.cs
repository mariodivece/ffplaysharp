namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;

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

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.SUBPICTURE_QUEUE_SIZE, false);
    }
}
