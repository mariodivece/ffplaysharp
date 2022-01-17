namespace FFmpeg;

public unsafe sealed class FFFilterGraph : UnmanagedCountedReference<AVFilterGraph>
{
    public FFFilterGraph([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
        : base(filePath, lineNumber)
    {
        Update(ffmpeg.avfilter_graph_alloc());
    }

    public FilterCollection Filters => new(this);

    public int ThreadCount
    {
        get => Pointer->nb_threads;
        set => Pointer->nb_threads = value;
    }

    public string? SoftwareScalerOptions
    {
        get => Helpers.PtrToString(Pointer->scale_sws_opts);
        set => Pointer->scale_sws_opts = value is null ? default : ffmpeg.av_strdup(value);
    }

    public void ParseLiteral(string graphLiteral, FFFilterInOut input, FFFilterInOut output)
    {
        var inputs = input.Pointer;
        var outputs = output.Pointer;
        var resultCode = ffmpeg.avfilter_graph_parse_ptr(Pointer, graphLiteral, &inputs, &outputs, null);
        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Could not parse filtergraph literal: {graphLiteral}");
    }

    public void Commit()
    {
        var resultCode = ffmpeg.avfilter_graph_config(Pointer, null);

        if (resultCode < 0)
            throw new FFmpegException(resultCode, "Could not commit filtergraph configuration.");
    }


    public int SetOption(string key, string value) =>
        ffmpeg.av_opt_set(Pointer, key, value, 0);

    protected override unsafe void ReleaseInternal(AVFilterGraph* pointer) =>
        ffmpeg.avfilter_graph_free(&pointer);
}
