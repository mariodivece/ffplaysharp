namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;

    public unsafe sealed class SubtitleDecoder : MediaDecoder
    {
        public SubtitleDecoder(MediaContainer container, AVCodecContext* codecContext)
            : base(container.Subtitle, codecContext)
        {
            Component = container.Subtitle;
        }

        public new SubtitleComponent Component { get; }

        public override void Start() => Start(SubtitleWorkerThreadMethod, nameof(SubtitleDecoder));

        private void SubtitleWorkerThreadMethod()
        {
            FrameHolder sp;
            var gotSubtitle = 0;
            double pts;

            while (true)
            {
                if ((sp = Component.Frames.PeekWriteable()) == null)
                    return; // 0;

                if ((gotSubtitle = DecodeFrame(out _, out var spsub)) < 0)
                    break;
                else
                    sp.SubtitlePtr = spsub;

                pts = 0;

                if (gotSubtitle != 0 && sp.SubtitlePtr->format == 0)
                {
                    if (sp.SubtitlePtr->pts != ffmpeg.AV_NOPTS_VALUE)
                        pts = sp.SubtitlePtr->pts / (double)ffmpeg.AV_TIME_BASE;
                    sp.Pts = pts;
                    sp.Serial = PacketSerial;
                    sp.Width = CodecContext->width;
                    sp.Height = CodecContext->height;
                    sp.uploaded = false;

                    /* now we can update the picture count */
                    Component.Frames.Push();
                }
                else if (gotSubtitle != 0)
                {
                    ffmpeg.avsubtitle_free(sp.SubtitlePtr);
                }
            }

            return; // 0
        }
    }
}
