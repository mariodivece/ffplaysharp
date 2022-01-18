namespace FFmpeg;

public unsafe sealed class FFFilterContext : NativeReference<AVFilterContext>
{
    private const int SearhChildrenFlags = ffmpeg.AV_OPT_SEARCH_CHILDREN;

    public FFFilterContext(AVFilterContext* target)
        : base(target)
    {
        // placeholder
    }

    public AVRational FrameRate => ffmpeg.av_buffersink_get_frame_rate(Target);

    public int SampleRate => ffmpeg.av_buffersink_get_sample_rate(Target);

    public int Channels => ffmpeg.av_buffersink_get_channels(Target);

    public long ChannelLayout => Convert.ToInt64(ffmpeg.av_buffersink_get_channel_layout(Target));

    public AVSampleFormat SampleFormat => (AVSampleFormat)ffmpeg.av_buffersink_get_format(Target);

    public AVRational TimeBase => ffmpeg.av_buffersink_get_time_base(Target);

    public static FFFilterContext Create(FFFilterGraph graph, string knownFilterName, string name, string? options = default)
    {
        var filter = FFFilter.FromName(knownFilterName);
        if (filter.IsNull())
            throw new ArgumentNullException(nameof(knownFilterName));

        return Create(graph, filter!, name, options);
    }

    public static FFFilterContext Create(FFFilterGraph graph, FFFilter filter, string name, string? options = default)
    {
        if (graph.IsNull())
            throw new ArgumentNullException(nameof(graph));

        if (filter.IsNull())
            throw new ArgumentNullException(nameof(graph));

        AVFilterContext* pointer = default;
        var resultCode = ffmpeg.avfilter_graph_create_filter(
            &pointer, filter.Target, name, options, null, graph.Target);

        return pointer is not null && resultCode >= 0
            ? new FFFilterContext(pointer)
            : throw new FFmpegException(resultCode, $"Failed to create filter context '{name}'");
    }

    public static void Link(FFFilterContext input, FFFilterContext output)
    {
        if (input.IsNull())
            throw new ArgumentNullException(nameof(input));

        if (output.IsNull())
            throw new ArgumentNullException(nameof(output));

        var resultCode = ffmpeg.avfilter_link(input.Target, 0, output.Target, 0);
        if (resultCode != 0)
            throw new FFmpegException(resultCode, "Failed to link filters.");
    }


    public void SetOption(string name, int value)
    {
        var resultCode = ffmpeg.av_opt_set_int(Target, name, value, SearhChildrenFlags);
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
        if (values is null)
            throw new ArgumentNullException(nameof(values));

        var pinnedValues = stackalloc T[values.Length];
        for (var i = 0; i < values.Length; i++)
            pinnedValues[i] = values[i];

        var resultCode = ffmpeg.av_opt_set_bin(Target, name, (byte*)pinnedValues, values.Length * sizeof(T), SearhChildrenFlags);
        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Could not set option list for '{name}'");
    }

    public int GetSinkFrame(FFFrame decodedFrame) =>
        ffmpeg.av_buffersink_get_frame_flags(Target, decodedFrame.Target, 0);

    public int AddSourceFrame(FFFrame decodedFrame) =>
        ffmpeg.av_buffersrc_add_frame(Target, decodedFrame.Target);
}
