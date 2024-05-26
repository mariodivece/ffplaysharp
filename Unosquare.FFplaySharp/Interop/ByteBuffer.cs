namespace Unosquare.FFplaySharp.Interop;

public unsafe sealed class ByteBuffer : CountedReference<byte>
{
    public ByteBuffer(ulong length, [CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(InteropExtensions.AllocateNativeMemory<byte>(length), filePath, lineNumber)
    {
        Length = length;
        AllocatedLength = length;
    }

    public ulong Length { get; private set; }

    public ulong AllocatedLength { get; private set; }

    public void Reallocate(ulong length)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (AllocatedLength < length)
        {
            var buffer = InteropExtensions.AllocateNativeMemory<byte>(length);
            if (!IsEmpty && Length > 0)
            {
                Buffer.MemoryCopy(this, buffer, Length, Length);
                DisposeNative(this);
                AllocatedLength = length;
                UpdatePointer(buffer);
            }
        }

        Length = length;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        NativeMemory.Clear(this, (nuint)AllocatedLength);
    }

    public Span<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new(this, (int)Length);
    }

    public void Write(byte* source, int length)
    {
        var maxLength = Math.Min(Convert.ToInt32(Length), length);
        Buffer.MemoryCopy(source, this, maxLength, maxLength);
    }

    protected override void DisposeNative(byte* target)
    {
        InteropExtensions.FreeNativeMemory(this);
        Length = 0;
        AllocatedLength = 0;
    }
}
