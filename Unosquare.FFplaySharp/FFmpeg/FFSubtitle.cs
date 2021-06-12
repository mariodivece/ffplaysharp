namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFSubtitle : UnmanagedCountedReference<AVSubtitle>
    {
        public FFSubtitle([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update((AVSubtitle*)ffmpeg.av_mallocz((ulong)sizeof(AVSubtitle)));
        }

        public long Pts
        {
            get => Pointer->pts;
        }

        public long StartDisplayTime => Pointer->start_display_time;

        public long EndDisplayTime => Pointer->end_display_time;

        public int Format => Pointer->format;

        public SubtitleRectCollection Rects => new(this);

        protected override unsafe void ReleaseInternal(AVSubtitle* pointer) =>
            ffmpeg.avsubtitle_free(pointer);
    }
}
