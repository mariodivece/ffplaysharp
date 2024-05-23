namespace FFmpeg;

public unsafe sealed class FFIOContext : NativeReference<AVIOContext>
{
    public FFIOContext(AVIOContext* target)
        : base(target)
    {
        // placeholder
    }

    public int Error => Reference->error;

    public long BytePosition => ffmpeg.avio_tell(this);

    public long Size => ffmpeg.avio_size(this);

    public bool TestEndOfStream() => ffmpeg.avio_feof(this) != 0;

    public bool EndOfStream
    {
        get => !IsEmpty && Reference->eof_reached != 0;
        set => Reference->eof_reached = (value) ? 1 : 0;
    }
}
