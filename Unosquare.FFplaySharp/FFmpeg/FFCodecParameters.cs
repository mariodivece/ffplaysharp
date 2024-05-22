namespace FFmpeg;

public unsafe sealed class FFCodecParameters : NativeReference<AVCodecParameters>
{
    public FFCodecParameters(AVCodecParameters* target)
        : base(target)
    {
        // placeholder
    }

    public AVMediaType CodecType => Reference->codec_type;

    public AVCodecID CodecId => Reference->codec_id;

    public int SampleRate => Reference->sample_rate;

    public int Channels => Reference->ch_layout.nb_channels;

    public int Width => Reference->width;

    public int Height => Reference->height;

    public AVRational SampleAspectRatio => Reference->sample_aspect_ratio;
}
