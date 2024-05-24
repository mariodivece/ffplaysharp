namespace FFmpeg;

public unsafe sealed class FFSubtitle : CountedReference<AVSubtitle>
{
    public FFSubtitle([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(InteropExtensions.AllocateNativeMemory<AVSubtitle>(), filePath, lineNumber)
    {
        // placeholder
    }

    public long Pts
    {
        get => Reference->pts;
    }

    public long StartDisplayTime => Reference->start_display_time;

    public long EndDisplayTime => Reference->end_display_time;

    public int Format => Reference->format;

    public SubtitleRectSet Rects => new(this);

    protected override unsafe void DisposeNative(AVSubtitle* target) =>
        ffmpeg.avsubtitle_free(target);
}
