namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
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

        public static FFFilterContext Create(FFFilterGraph graph, FFFilter filter, string name, string options)
        {
            AVFilterContext* pointer = null;
            var resultCode = ffmpeg.avfilter_graph_create_filter(
                &pointer, filter.Pointer, name, options, null, graph.Pointer);

            var result = pointer != null ? new FFFilterContext(pointer) : null;

            if (resultCode < 0 || result == null)
                throw new FFmpegException(resultCode, $"Failed to create filter context '{name}'");

            return result;
        }

        public static void Link(FFFilterContext input, FFFilterContext output)
        {
            var resultCode = ffmpeg.avfilter_link(input.Pointer, 0, output.Pointer, 0);
            if (resultCode != 0)
                throw new FFmpegException(resultCode, "Failed to link filters.");
        }


        public void SetOption(string name, long value)
        {
            var resultCode = ffmpeg.av_opt_set_int(Pointer, name, value, SearhChildrenFlags);
            if (resultCode < 0)
                throw new FFmpegException(resultCode, $"Failed to set option '{name}'.");
        }
            


        /// <summary>
        /// Port of av_opt_set_int_list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public void SetOptionList<T>(string name, T[] values)
            where T : unmanaged
        {
            var pinnedValues = stackalloc T[values.Length];
            for (var i = 0; i < values.Length; i++)
                pinnedValues[i] = values[i];

            var resultCode = ffmpeg.av_opt_set_bin(Pointer, name, (byte*)pinnedValues, values.Length * sizeof(T), SearhChildrenFlags);
            if (resultCode < 0)
                throw new FFmpegException(resultCode, $"Could not set option list for '{name}'");
        }

        public int GetSinkFrame(FFFrame decodedFrame) =>
            ffmpeg.av_buffersink_get_frame_flags(Pointer, decodedFrame.Pointer, 0);

        public int AddSourceFrame(FFFrame decodedFrame) =>
            ffmpeg.av_buffersrc_add_frame(Pointer, decodedFrame.Pointer);
    }
}
