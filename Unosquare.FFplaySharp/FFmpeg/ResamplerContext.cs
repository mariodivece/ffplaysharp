namespace FFmpeg;

public unsafe sealed class ResamplerContext : CountedReference<SwrContext>
{
    public ResamplerContext([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        Update(ffmpeg.swr_alloc());
    }

    public ResamplerContext(
        AVChannelLayout outLayout,
        AVSampleFormat outFormat,
        int outSampleRate,
        AVChannelLayout inLayout,
        AVSampleFormat inFormat,
        int inSampleRate,
        [CallerFilePath] string? filePath = default,
        [CallerLineNumber] int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        var pointer = ffmpeg.swr_alloc();

        ffmpeg.swr_alloc_set_opts2(&pointer,
                &outLayout, outFormat, outSampleRate,
                &inLayout, inFormat, inSampleRate,
                0, null);

        Update(pointer);
    }

    public int Convert(byte** output, int outputCount, byte** input, int inputCount) =>
        ffmpeg.swr_convert(Target, output, outputCount, input, inputCount);

    public int SetCompensation(int delta, int distance) =>
        ffmpeg.swr_set_compensation(Target, delta, distance);

    public int Initialize() =>
        ffmpeg.swr_init(Target);

    public int SetOption(string key, string value) =>
        ffmpeg.av_opt_set(Target, key, value, 0);

    protected override unsafe void ReleaseInternal(SwrContext* pointer) =>
        ffmpeg.swr_free(&pointer);
}
