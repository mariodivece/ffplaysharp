namespace FFmpeg;

public unsafe sealed class FFIOContext : NativeReference<AVIOContext>
{
    public FFIOContext(AVIOContext* target)
        : base(target)
    {
        // placeholder
    }

    public int Error => Reference->error;

    public long BytePosition => ffmpeg.avio_tell(Reference);

    public long Size => ffmpeg.avio_size(Reference);

    public bool TestEndOfStream() => ffmpeg.avio_feof(Reference) != 0;

    public bool EndOfStream
    {
        get => Address.IsNotNull() && Reference->eof_reached != 0;
        set => Reference->eof_reached = (value) ? 1 : 0;
    }
}
