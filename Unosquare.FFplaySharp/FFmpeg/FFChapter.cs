using Unosquare.FFplaySharp.Interop;

namespace FFmpeg;

public unsafe sealed class FFChapter : NativeReference<AVChapter>
{
    public FFChapter(AVChapter* target)
        : base(target)
    {
        // placeholder
    }

    public long StartTime => Reference->start;

    public AVRational TimeBase => Reference->time_base;
}
