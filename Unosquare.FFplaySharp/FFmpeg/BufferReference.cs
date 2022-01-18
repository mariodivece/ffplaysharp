namespace FFmpeg;

public unsafe class BufferReference : NativeReference<byte>
{
    public BufferReference(byte* target, long length)
        : base(target)
    {
        Length = length;
    }

    public long Length { get; set; }
}
