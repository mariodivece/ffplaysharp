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

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.SUBPICTURE_QUEUE_SIZE, false);

        private int DecodeFrame(out AVSubtitle* frame) => DecodeFrame(out _, out frame);

        protected override void DecodingThreadMethod()
        {
            while (true)
            {
                var queuedFrame = Frames.PeekWriteable();
                if (queuedFrame == null) break;

                var gotSubtitle = DecodeFrame(out var decodedFrame);
                if (gotSubtitle < 0) break;

                queuedFrame.SubtitlePtr = decodedFrame;

                if (gotSubtitle != 0 && queuedFrame.SubtitlePtr->format == 0)
                {
                    queuedFrame.Pts = queuedFrame.SubtitlePtr->pts.IsValidPts()
                        ? queuedFrame.SubtitlePtr->pts / (double)ffmpeg.AV_TIME_BASE : 0;
                    queuedFrame.Serial = PacketSerial;
                    queuedFrame.Width = CodecContext->width;
                    queuedFrame.Height = CodecContext->height;
                    queuedFrame.uploaded = false;

                    // now we can update the picture count
                    Frames.Push();
                }
                else if (gotSubtitle != 0)
                {
                    ffmpeg.avsubtitle_free(queuedFrame.SubtitlePtr);
                }
            }
        }
    }
}
