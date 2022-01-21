namespace FFmpeg;

public unsafe sealed class FFFilterGraph : CountedReference<AVFilterGraph>
{
    public FFFilterGraph([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        Update(ffmpeg.avfilter_graph_alloc());
    }

    public FilterSet Filters => new(this);

    public int ThreadCount
    {
        get => Target->nb_threads;
        set => Target->nb_threads = value;
    }

    public string? SoftwareScalerOptions
    {
        get => Helpers.PtrToString(Target->scale_sws_opts);
        set => Target->scale_sws_opts = value is null ? default : ffmpeg.av_strdup(value);
    }

    public void ParseLiteral(string graphLiteral, FFFilterInOut input, FFFilterInOut output)
    {
        var inputs = input.Target;
        var outputs = output.Target;
        var resultCode = ffmpeg.avfilter_graph_parse_ptr(Target, graphLiteral, &inputs, &outputs, null);
        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Could not parse filtergraph literal: {graphLiteral}");
    }

    public void Commit()
    {
        var resultCode = ffmpeg.avfilter_graph_config(Target, null);

        if (resultCode < 0)
            throw new FFmpegException(resultCode, "Could not commit filtergraph configuration.");
    }


    public int SetOption(string key, string value) =>
        ffmpeg.av_opt_set(Target, key, value, 0);

    protected override unsafe void ReleaseInternal(AVFilterGraph* target) =>
        ffmpeg.avfilter_graph_free(&target);
}
