namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class SubtitleComponent : MediaComponent
    {
        public SubtitleComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwsContext* ConvertContext;

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_SUBTITLE;

        public override string WantedCodecName => Container.Options.AudioForcedCodecName;

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.SubtitleFrameQueueCapacity, false);

        private int DecodeFrame(out AVSubtitle* frame) => DecodeFrame(out _, out frame);

        protected override void DecodingThreadMethod()
        {
            while (true)
            {
                var gotSubtitle = DecodeFrame(out var decodedFrame);
                if (gotSubtitle < 0) break;
                
                if (gotSubtitle != 0 && decodedFrame->format == 0)
                {
                    var queuedFrame = Frames.PeekWriteable();
                    if (queuedFrame == null) break;

                    var frameTime = decodedFrame->pts.IsValidPts()
                        ? decodedFrame->pts / Clock.TimeBaseMicros : 0;

                    // now we can update the picture count
                    queuedFrame.Update(decodedFrame, CodecContext, PacketSerial, frameTime);
                    Frames.Enqueue();
                }
                else if (gotSubtitle != 0)
                {
                    ffmpeg.avsubtitle_free(decodedFrame);
                }
            }
        }
    }
}
