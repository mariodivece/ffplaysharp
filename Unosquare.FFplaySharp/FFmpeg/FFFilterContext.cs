namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using Unosquare.FFplaySharp;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFilterContext : UnmanagedReference<AVFilterContext>
    {
        private const int SearhChildrenFlags = ffmpeg.AV_OPT_SEARCH_CHILDREN;

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

        public static (FFFilterContext result, int resultCode) Create(FFFilterGraph graph, FFFilter filter, string name, string options)
        {
            AVFilterContext* pointer = null;
            var resultCode = ffmpeg.avfilter_graph_create_filter(
                &pointer, filter.Pointer, name, options, null, graph.Pointer);

            var result = pointer != null ? new FFFilterContext(pointer) : null;
            return (result, resultCode);
        }

        public static int Link(FFFilterContext input, FFFilterContext output) =>
            ffmpeg.avfilter_link(input.Pointer, 0, output.Pointer, 0);

        public int SetOption(string name, long value) =>
            ffmpeg.av_opt_set_int(Pointer, name, value, SearhChildrenFlags);

        public int SetOptionList<T>(string name, T[] values)
            where T : unmanaged
        {
            var pinnedValues = stackalloc T[values.Length];
            for (var i = 0; i < values.Length; i++)
                pinnedValues[i] = values[i];

            return ffmpeg.av_opt_set_bin(Pointer, name, (byte*)pinnedValues, values.Length * sizeof(T), SearhChildrenFlags);
        }

        public int GetSinkFlags(AVFrame* decodedFrame) => ffmpeg.av_buffersink_get_frame_flags(Pointer, decodedFrame, 0);

        public int AddFrame(AVFrame* decodedFrame) => ffmpeg.av_buffersrc_add_frame(Pointer, decodedFrame);
    }
}
