namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class Dictionary : UnmanagedReference<AVDictionary>
    {
        public Dictionary([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            // placeholder
        }

        protected override unsafe void ReleaseInternal(AVDictionary* pointer) =>
            ffmpeg.av_dict_free(&pointer);
    }
}
