namespace FFmpeg;

public unsafe sealed class FFChapter : NativeReference<AVChapter>
{
    public FFChapter(AVChapter* target)
        : base(target)
    {
        // placeholder
    }

    public long StartTime => Target->start;

    public AVRational TimeBase => Target->time_base;
}
