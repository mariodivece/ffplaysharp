namespace FFmpeg;

public unsafe sealed class FFCodecContext : CountedReference<AVCodecContext>
{
    public FFCodecContext([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        UpdatePointer(ffmpeg.avcodec_alloc_context3(null));
    }

    public AVRational PacketTimeBase
    {
        get => Reference->pkt_timebase;
        set => Reference->pkt_timebase = value;
    }

    public AVCodecID CodecId
    {
        get => Reference->codec_id;
        set => Reference->codec_id = value;
    }

    public string CodecName => FFCodec.GetName(CodecId);

    public int LowResFactor
    {
        get => Reference->lowres;
        set => Reference->lowres = value;
    }

    public int Flags2
    {
        get => Reference->flags2;
        set => Reference->flags2 = value;
    }

    public AVMediaType CodecType => Reference->codec_type;

    public int Width => Reference->width;

    public int Height => Reference->height;

    public int SampleRate => Reference->sample_rate;

    public int Channels => Reference->ch_layout.nb_channels;

    public AVChannelLayout ChannelLayout => Reference->ch_layout;

    public AVSampleFormat SampleFormat => Reference->sample_fmt;

    public void ApplyStreamParameters(FFStream stream)
    {
        if (stream is null || stream.IsVoid())
            throw new ArgumentNullException(nameof(stream));

        var resultCode = ffmpeg.avcodec_parameters_to_context(this, stream.CodecParameters);
        if (resultCode < 0)
            throw new FFmpegException(resultCode, "Unable to apply stream parameters");
    }

    public int ReceiveFrame(FFFrame frame) => frame is null || frame.IsVoid()
        ? throw new ArgumentNullException(nameof(frame))
        : ffmpeg.avcodec_receive_frame(this, frame);

    public int DecodeSubtitle(FFSubtitle frame, FFPacket packet, ref int gotSubtitle)
    {
        int gotResult;
        var resultCode = ffmpeg.avcodec_decode_subtitle2(
            Reference,
            frame,
            &gotResult,
            packet);

        gotSubtitle = gotResult;
        return resultCode;
    }

    public void FlushBuffers() => ffmpeg.avcodec_flush_buffers(this);

    public int SendPacket(FFPacket packet) => ffmpeg.avcodec_send_packet(this, packet);

    public void Open(FFCodec codec, FFDictionary codecOptions)
    {
        ArgumentNullException.ThrowIfNull((object?)codecOptions);

        if (codec.IsVoid())
            throw new ArgumentNullException(nameof(codec));

        var codecOptionsArg = codecOptions.Reference;
        var resultCode = ffmpeg.avcodec_open2(this, codec, &codecOptionsArg);
        codecOptions.UpdatePointer(codecOptionsArg);

        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Could not open codec '{codec.Name}'");
    }

    protected override unsafe void ReleaseNative(AVCodecContext* target) =>
        ffmpeg.avcodec_free_context(&target);
}
