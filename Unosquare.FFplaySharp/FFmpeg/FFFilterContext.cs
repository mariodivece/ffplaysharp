namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using Unosquare.FFplaySharp;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFilterContext : UnmanagedReference<AVFilterContext>
    {
        public FFFilterContext(AVFilterContext* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public AVRational FrameRate => ffmpeg.av_buffersink_get_frame_rate(Pointer);

        public int SampleRate => ffmpeg.av_buffersink_get_sample_rate(Pointer);

        public int Channels => ffmpeg.av_buffersink_get_channels(Pointer);

        public long ChannelLayout => Convert.ToInt64(ffmpeg.av_buffersink_get_channel_layout(Pointer));

        public AVSampleFormat SampleFormat => (AVSampleFormat)ffmpeg.av_buffersink_get_format(Pointer);

        public AVRational TimeBase => ffmpeg.av_buffersink_get_time_base(Pointer);

        public int GetSinkFlags(AVFrame* decodedFrame) => ffmpeg.av_buffersink_get_frame_flags(Pointer, decodedFrame, 0);

        public int AddFrame(AVFrame* decodedFrame) => ffmpeg.av_buffersrc_add_frame(Pointer, decodedFrame);
    }
}
