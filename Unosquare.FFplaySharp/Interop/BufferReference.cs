namespace Unosquare.FFplaySharp.Interop;

public unsafe class BufferReference : NativeReference<byte>
{
    public static readonly BufferReference NullBuffer = new(null, 1);

    public BufferReference(byte* target, long length)
        : base(target)
    {
        Length = length;
    }

    public long Length { get; set; }
}
