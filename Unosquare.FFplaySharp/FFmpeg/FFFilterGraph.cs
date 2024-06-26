﻿namespace FFmpeg;

public unsafe sealed class FFFilterGraph : CountedReference<AVFilterGraph>
{
    public FFFilterGraph([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(ffmpeg.avfilter_graph_alloc(), filePath, lineNumber)
    {
        // placeholder
    }

    public FilterSet Filters => new(this);

    public int ThreadCount
    {
        get => Reference->nb_threads;
        set => Reference->nb_threads = value;
    }

    public string? SoftwareScalerOptions
    {
        get => Helpers.PtrToString(Reference->scale_sws_opts);
        set => Reference->scale_sws_opts = value is null ? default : ffmpeg.av_strdup(value);
    }

    public void ParseLiteral(string graphLiteral, FFFilterInOut input, FFFilterInOut output)
    {
        if (input is null || input.IsVoid())
            throw new ArgumentNullException(nameof(input));

        if (output is null || output.IsVoid())
            throw new ArgumentNullException(nameof(output));

        using var inputPtr = input.AsDoublePointer();
        using var outputPtr = output.AsDoublePointer();
        var resultCode = ffmpeg.avfilter_graph_parse_ptr(this, graphLiteral, inputPtr, outputPtr, null);

        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Could not parse filtergraph literal: {graphLiteral}");
    }

    public void Commit()
    {
        var resultCode = ffmpeg.avfilter_graph_config(this, null);

        if (resultCode < 0)
            throw new FFmpegException(resultCode, "Could not commit filtergraph configuration.");
    }


    public int SetOption(string key, string value) =>
        ffmpeg.av_opt_set(this, key, value, 0);

    protected override void DisposeNative(AVFilterGraph* target) =>
        ffmpeg.avfilter_graph_free(&target);
}
