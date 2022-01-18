namespace FFmpeg;

public unsafe sealed class FFCodecContext : CountedReference<AVCodecContext>
{
    public FFCodecContext([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
        : base(filePath, lineNumber)
    {
        Update(ffmpeg.avcodec_alloc_context3(null));
    }

    public AVRational PacketTimeBase
    {
        get => Target->pkt_timebase;
        set => Target->pkt_timebase = value;
    }

    public AVCodecID CodecId
    {
        get => Target->codec_id;
        set => Target->codec_id = value;
    }

    public string CodecName => FFCodec.GetName(CodecId);

    public int LowResFactor
    {
        get => Target->lowres;
        set => Target->lowres = value;
    }

    public int Flags2
    {
        get => Target->flags2;
        set => Target->flags2 = value;
    }

    public AVMediaType CodecType => Target->codec_type;

    public int Width => Target->width;

    public int Height => Target->height;

    public long FaultyPtsCount => Target->pts_correction_num_faulty_pts;

    public long FaultyDtsCount => Target->pts_correction_num_faulty_dts;

    public int SampleRate => Target->sample_rate;

    public int Channels => Target->channels;

    public long ChannelLayout => Convert.ToInt64(Target->channel_layout);

    public AVSampleFormat SampleFormat => Target->sample_fmt;

    public void ApplyStreamParameters(FFStream stream)
    {
        var resultCode = ffmpeg.avcodec_parameters_to_context(Target, stream.CodecParameters.Target);
        if (resultCode < 0)
            throw new FFmpegException(resultCode, "Unable to apply stream parameters");
    }

    public int ReceiveFrame(FFFrame frame) =>
        ffmpeg.avcodec_receive_frame(Target, frame.Target);

    public int DecodeSubtitle(FFSubtitle frame, FFPacket packet, ref int gotSubtitle)
    {
        int gotResult;
        var resultCode = ffmpeg.avcodec_decode_subtitle2(
            Target,
            frame.Target,
            &gotResult,
            packet.Target);

        gotSubtitle = gotResult;
        return resultCode;
    }

    public void FlushBuffers() => ffmpeg.avcodec_flush_buffers(Target);

    public int SendPacket(FFPacket packet) => ffmpeg.avcodec_send_packet(Target, packet.Target);

    public void Open(FFCodec codec, FFDictionary codecOptions)
    {
        if (codecOptions is null)
            throw new ArgumentNullException(nameof(codecOptions));

        if (codec.IsNull())
            throw new ArgumentNullException(nameof(codec));

        var codecOptionsArg = codecOptions.Target;
        var resultCode = ffmpeg.avcodec_open2(Target, codec.Target, &codecOptionsArg);
        codecOptions.Update(codecOptionsArg);

        if (resultCode < 0)
            throw new FFmpegException(resultCode, $"Could not open codec '{codec.Name}'");
    }

    protected override unsafe void ReleaseInternal(AVCodecContext* target) =>
        ffmpeg.avcodec_free_context(&target);
}
