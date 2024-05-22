namespace FFmpeg;

public unsafe sealed class FFFilterContext : NativeReference<AVFilterContext>
{
    private const int SearhChildrenFlags = ffmpeg.AV_OPT_SEARCH_CHILDREN;

    public FFFilterContext(AVFilterContext* target)
        : base(target)
    {
        // placeholder
    }

    public AVRational FrameRate => ffmpeg.av_buffersink_get_frame_rate(Reference);

    public int SampleRate => ffmpeg.av_buffersink_get_sample_rate(Reference);

    public int Channels => ffmpeg.av_buffersink_get_channels(Reference);

    public AVChannelLayout ChannelLayout
    {
        get
        {
            AVChannelLayout layout = default;
            ffmpeg.av_buffersink_get_ch_layout(this, &layout);
            return layout;
        }

    }

    public AVSampleFormat SampleFormat => (AVSampleFormat)ffmpeg.av_buffersink_get_format(this);

    public AVRational TimeBase => ffmpeg.av_buffersink_get_time_base(this);

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
            &pointer, filter, name, options, null, graph);

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

        var resultCode = ffmpeg.avfilter_link(input, 0, output, 0);
        if (resultCode != 0)
            throw new FFmpegException(resultCode, "Failed to link filters.");
    }


    public void SetOption(string name, int value)
    {
        var resultCode = ffmpeg.av_opt_set_int(this, name, value, SearhChildrenFlags);
        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Failed to set option '{name}'.");
    }

    public void SetOption(string name, string value)
    {
        var resultCode = ffmpeg.av_opt_set(this, name, value, SearhChildrenFlags);
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
        ArgumentNullException.ThrowIfNull(values);
        var pinnedValues = stackalloc T[values.Length];
        for (var i = 0; i < values.Length; i++)
            pinnedValues[i] = values[i];

        var resultCode = ffmpeg.av_opt_set_bin(this, name, (byte*)pinnedValues, values.Length * sizeof(T), SearhChildrenFlags);
        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Could not set option list for '{name}'");
    }

    public int GetSinkFrame(FFFrame decodedFrame) =>
        ffmpeg.av_buffersink_get_frame_flags(this, decodedFrame, 0);

    public int AddSourceFrame(FFFrame decodedFrame) =>
        ffmpeg.av_buffersrc_add_frame(this, decodedFrame);
}
