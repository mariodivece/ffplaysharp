namespace FFmpeg;

public unsafe sealed class FFOption : NativeReference<AVOption>
{
    public FFOption(AVOption* target)
        : base(target)
    {

    }

    public AVOptionType Type => Reference->type;
}
