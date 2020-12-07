﻿namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public unsafe class FrameHolder : IDisposable
    {
        public FrameHolder()
        {
            FramePtr = ffmpeg.av_frame_alloc();
        }

        public AVFrame* FramePtr { get; private set; }

        public AVSubtitle* SubtitlePtr { get; set; }

        public int Serial;
        public double Pts;           /* presentation timestamp for the frame */
        public double Duration;      /* estimated duration of the frame */
        public long Position;          /* byte position of the frame in the input file */
        public int Width;
        public int Height;
        public int Format;
        public AVRational Sar;
        public bool uploaded;
        public bool FlipVertical;

        public void Unreference()
        {
            if (FramePtr != null)
                ffmpeg.av_frame_unref(FramePtr);

            if (SubtitlePtr != null)
                ffmpeg.avsubtitle_free(SubtitlePtr);
        }

        public void Dispose()
        {
            var framePtr = FramePtr;
            if (framePtr != null)
            {
                ffmpeg.av_frame_unref(FramePtr);
                ffmpeg.av_frame_free(&framePtr);
                FramePtr = null;
            }

            if (SubtitlePtr != null)
                ffmpeg.avsubtitle_free(SubtitlePtr);

            SubtitlePtr = null;
        }
    }
}