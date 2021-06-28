namespace FFmpeg
{
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class BufferReference : UnmanagedReference<byte>
    {
        public BufferReference(byte* pointer, long length)
            : base(pointer)
        {
            Length = length;
        }

        public long Length { get; set; }
    }
}
