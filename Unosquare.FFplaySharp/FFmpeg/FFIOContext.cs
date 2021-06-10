namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFIOContext : UnmanagedReference<AVIOContext>
    {
        public FFIOContext(AVIOContext* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public int Error => Pointer->error;

        public long BytePosition => ffmpeg.avio_tell(Pointer);

        public long Size => ffmpeg.avio_size(Pointer);

        public int TestEndOfStream() => ffmpeg.avio_feof(Pointer);

        public bool EndOfStream
        {
            get => IsNull ? false : Pointer->eof_reached != 0;
            set => Pointer->eof_reached = (value) ? 1 : 0;
        }
    }
}
