namespace FFmpeg;

public unsafe sealed class FFCodecParameters : UnmanagedReference<AVCodecParameters>
{
    public FFCodecParameters(AVCodecParameters* pointer)
        : base(pointer)
    {
        // placeholder
    }

    public AVMediaType CodecType => Pointer->codec_type;

    public AVCodecID CodecId => Pointer->codec_id;

    public int SampleRate => Pointer->sample_rate;

    public int Channels => Pointer->channels;

    public int Width => Pointer->width;

    public int Height => Pointer->height;

    public AVRational SampleAspectRatio => Pointer->sample_aspect_ratio;
}
