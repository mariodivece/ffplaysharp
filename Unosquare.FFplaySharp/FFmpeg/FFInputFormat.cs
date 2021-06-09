namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFInputFormat : UnmanagedReference<AVInputFormat>
    {
        public static readonly FFInputFormat None = new(null);

        private FFInputFormat(AVInputFormat* pointer)
        {
            Update(pointer);
        }

        public static FFInputFormat Find(string shortName)
        {
            var pointer = ffmpeg.av_find_input_format(shortName);
            return new(pointer);
        }
    }
}
