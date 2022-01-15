namespace FFmpeg;

public unsafe sealed class FFOption : UnmanagedReference<AVOption>
{
    public FFOption(AVOption* pointer)
        : base(pointer)
    {

    }

    public AVOptionType Type => Pointer->type;
}
