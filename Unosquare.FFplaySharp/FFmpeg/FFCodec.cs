namespace FFmpeg;

public unsafe sealed class FFCodec : NativeReference<AVCodec>
{
    public FFCodec(AVCodec* target)
        : base(target)
    {
        // placeholder
    }

    public AVCodecID Id => Target->id;

    public FFMediaClass PrivateClass =>
        FFMediaClass.FromPrivateClass(Target->priv_class)!;

    public int MaxLowResFactor => Target->max_lowres;

    public string? Name => Address.IsNull() ? default : GetName(Target->id);

    public static string GetName(AVCodecID codecId) => ffmpeg.avcodec_get_name(codecId);

    public static FFCodec? FromDecoderId(AVCodecID codecId)
    {
        var pointer = ffmpeg.avcodec_find_decoder(codecId);
        return pointer is not null ? new(pointer) : default;
    }

    public static FFCodec? FromEncoderId(AVCodecID codecId)
    {
        var pointer = ffmpeg.avcodec_find_decoder(codecId);
        return pointer is not null ? new(pointer) : default;
    }

    public static FFCodec? FromDecoderName(string name)
    {
        var pointer = ffmpeg.avcodec_find_decoder_by_name(name);
        return pointer is not null ? new(pointer) : default;
    }
}
