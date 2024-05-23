namespace FFmpeg;

public unsafe sealed class FFFilter : NativeReference<AVFilter>
{
    private FFFilter(AVFilter* pointer)
        : base(pointer)
    {
        // placeholder
    }

    public static FFFilter? FromName(string name)
    {
        var pointer = ffmpeg.avfilter_get_by_name(name);
        return pointer is not null ? new FFFilter(pointer) : default;
    }
}
