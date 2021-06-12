namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using System;

    public unsafe sealed class FrameHolder : IDisposable, ISerialGroupable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FrameHolder" /> class.
        /// </summary>
        public FrameHolder()
        {
            FramePtr = new FFFrame();
        }

        public FFFrame FramePtr { get; private set; }

        public AVSubtitle* SubtitlePtr { get; private set; }

        public int GroupIndex { get; private set; }

        /// <summary>
        /// Gets or sets the Presentation time in seconds.
        /// This is NOT a timestamp in stream units.
        /// </summary>
        public double Time { get; private set; }

        /// <summary>
        /// Gets the estimated duration of the frame in seconds.
        /// </summary>
        public double Duration { get; private set; }

        /// <summary>
        /// Gets the byte position of the frame in the input file.
        /// This is extracted from the packet position.
        /// </summary>
        public long Position => FramePtr.PacketPosition;

        /// <summary>
        /// Gets the video frame width in pixels.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the video frame height in pixels.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets the sample aspect ratio of the video frame.
        /// </summary>
        public AVRational Sar => FramePtr.SampleAspectRatio;

        /// <summary>
        /// Gets whether the frame has been marked as uploaded to the renderer.
        /// </summary>
        public bool IsUploaded { get; private set; }

        /// <summary>
        /// Gets whether the video frame is flipped vertically.
        /// </summary>
        public bool FlipVertical => FramePtr != null && FramePtr.LineSize[0] < 0;

        public AVSampleFormat SampleFormat => FramePtr.SampleFormat;

        public string SampleFormatName => AudioParams.GetSampleFormatName(SampleFormat);

        public AVPixelFormat PixelFormat => FramePtr.PixelFormat;

        public byte_ptrArray8 PixelData => FramePtr.Data;

        public int_array8 PixelStride => FramePtr.LineSize;

        public int Channels => FramePtr.Channels;

        public int Frequency => FramePtr.SampleRate;

        public int SampleCount => FramePtr.SampleCount;

        public bool HasValidTime => !Time.IsNaN();

        public double StartDisplayTime => SubtitlePtr != null
            ? Time + (SubtitlePtr->start_display_time / 1000d)
            : Time;

        public double EndDisplayTime => SubtitlePtr != null
            ? Time + (SubtitlePtr->end_display_time / 1000d)
            : Time + Duration;

        public void MarkUploaded() => IsUploaded = true;

        public void Update(FFFrame sourceFrame, int groupIndex, double time, double duration)
        {
            sourceFrame.MoveTo(FramePtr);
            IsUploaded = false;
            GroupIndex = groupIndex;
            Time = time;
            Duration = duration;
            Width = FramePtr.Width;
            Height = FramePtr.Height;
        }

        public void Update(AVSubtitle* sourceFrame, FFCodecContext codecContext, int groupIndex, double time)
        {
            if (SubtitlePtr != null)
                ffmpeg.avsubtitle_free(SubtitlePtr);
            
            SubtitlePtr = sourceFrame;
            IsUploaded = false;
            GroupIndex = groupIndex;
            Time = time;
            Duration = (SubtitlePtr->end_display_time - SubtitlePtr->start_display_time) / 1000d;
            Width = codecContext.Width;
            Height = codecContext.Height;
        }

        public void UpdateSubtitleArea(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public void Unreference()
        {
            FramePtr?.Reset();

            if (SubtitlePtr != null)
            {
                ffmpeg.avsubtitle_free(SubtitlePtr);
                SubtitlePtr = null;
            }
        }

        public void Dispose()
        {
            FramePtr?.Release();
            FramePtr = null;

            if (SubtitlePtr != null)
            {
                ffmpeg.avsubtitle_free(SubtitlePtr);
                SubtitlePtr = null;
            }

            SubtitlePtr = null;
        }
    }
}
