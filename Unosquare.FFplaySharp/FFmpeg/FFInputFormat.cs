namespace FFmpeg;

public unsafe sealed class FFInputFormat : UnmanagedReference<AVInputFormat>
{
    public static readonly FFInputFormat None = new(null);

    public FFInputFormat(AVInputFormat* pointer)
    {
        Update(pointer);
    }

    public int Flags => Pointer->flags;

    public string Name => Helpers.PtrToString(Pointer->name);

    public static FFInputFormat Find(string shortName)
    {
        var pointer = ffmpeg.av_find_input_format(shortName);
        return new(pointer);
    }
}
