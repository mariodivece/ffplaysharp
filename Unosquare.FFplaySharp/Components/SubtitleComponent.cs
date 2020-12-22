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

        protected override void WorkerThreadMethod()
        {
            FrameHolder decodedFrame;
            var gotSubtitle = 0;
            double pts;

            while (true)
            {
                if ((decodedFrame = Frames.PeekWriteable()) == null)
                    return; // 0;

                if ((gotSubtitle = DecodeFrame(out _, out var outputFrame)) < 0)
                    break;
                else
                    decodedFrame.SubtitlePtr = outputFrame;

                pts = 0;

                if (gotSubtitle != 0 && decodedFrame.SubtitlePtr->format == 0)
                {
                    if (decodedFrame.SubtitlePtr->pts != ffmpeg.AV_NOPTS_VALUE)
                        pts = decodedFrame.SubtitlePtr->pts / (double)ffmpeg.AV_TIME_BASE;
                    decodedFrame.Pts = pts;
                    decodedFrame.Serial = PacketSerial;
                    decodedFrame.Width = CodecContext->width;
                    decodedFrame.Height = CodecContext->height;
                    decodedFrame.uploaded = false;

                    /* now we can update the picture count */
                    Frames.Push();
                }
                else if (gotSubtitle != 0)
                {
                    ffmpeg.avsubtitle_free(decodedFrame.SubtitlePtr);
                }
            }

            return; // 0
        }
    }
}
