namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFChapter : UnmanagedReference<AVChapter>
    {
        public FFChapter(AVChapter* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public long StartTime => Pointer->start;

        public AVRational TimeBase => Pointer->time_base;
    }
}
