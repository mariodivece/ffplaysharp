namespace FFmpeg;

public unsafe sealed class ByteBuffer : CountedReference<byte>
{
    public ByteBuffer(ulong length, [CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        var pointer = (byte*)ffmpeg.av_mallocz(length);
        UpdatePointer(pointer);
        Length = length;
    }

    public ulong Length { get; private set; }

    public static ByteBuffer Reallocate(ByteBuffer? original, ulong length)
    {
        if (original is null || original.IsVoid() || original.Length < length)
        {
            original?.Dispose();
            return new(length);
        }

        return original;
    }

    public void Write(byte* source, int length)
    {
        var maxLength = Math.Min(Convert.ToInt32(Length), length);
        Buffer.MemoryCopy(source, this, maxLength, maxLength);
    }

    protected override void ReleaseNative(byte* target)
    {
        ffmpeg.av_free(target);
        Length = 0;
    }
}
