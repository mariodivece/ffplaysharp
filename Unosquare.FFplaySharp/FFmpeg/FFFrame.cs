namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFrame : UnmanagedCountedReference<AVFrame>
    {
        public FFFrame([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.av_frame_alloc());
        }

        public long PacketPosition => Pointer->pkt_pos;

        public AVRational SampleAspectRatio
        {
            get => Pointer->sample_aspect_ratio;
            set => Pointer->sample_aspect_ratio = value;
        }

        public AVSampleFormat SampleFormat => (AVSampleFormat)Pointer->format;

        public string SampleFormatName => AudioParams.GetSampleFormatName(SampleFormat);

        public AVPixelFormat PixelFormat => (AVPixelFormat)Pointer->format;

        public int_array8 LineSize => Pointer->linesize;

        public int_array8 PixelStride => LineSize;

        public byte_ptrArray8 Data => Pointer->data;

        public int Width => Pointer->width;

        public int Height => Pointer->height;

        public int SampleCount => Pointer->nb_samples;

        public int Channels => Pointer->channels;

        public int SampleRate => Pointer->sample_rate;

        public double AudioComputedDuration => (double)SampleCount / SampleRate;

        public long Pts
        {
            get => Pointer->pts;
            set => Pointer->pts = value;
        }

        public long PacketDts => Pointer->pkt_dts;

        public long BestEffortPts => Pointer->best_effort_timestamp;

        public byte** ExtendedData
        {
            get => Pointer->extended_data;
            set => Pointer->extended_data = value;
        }

        public byte** AudioData
        {
            get => ExtendedData;
            set => ExtendedData = value;
        }

        public long ChannelLayout => Convert.ToInt64(Pointer->channel_layout);

        public void Reset()
        {
            if (IsNull)
                return;

            ffmpeg.av_frame_unref(Pointer);
        }

        public void MoveTo(FFFrame destination) =>
            ffmpeg.av_frame_move_ref(destination.Pointer, Pointer);

        protected override unsafe void ReleaseInternal(AVFrame* pointer) =>
            ffmpeg.av_frame_free(&pointer);
    }
}
