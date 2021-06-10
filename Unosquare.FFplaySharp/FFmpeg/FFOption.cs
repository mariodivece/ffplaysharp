namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFOption : UnmanagedReference<AVOption>
    {
        public FFOption(AVOption* pointer)
            : base(pointer)
        {

        }

        public AVOptionType Type => Pointer->type;
    }
}
