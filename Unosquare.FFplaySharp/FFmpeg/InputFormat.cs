namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class InputFormat : UnmanagedReference<AVInputFormat>
    {
        public static readonly InputFormat None = new(null);

        private InputFormat(AVInputFormat* pointer)
        {
            Update(pointer);
        }

        public static InputFormat Find(string shortName)
        {
            var pointer = ffmpeg.av_find_input_format(shortName);
            return new(pointer);
        }
    }
}
