namespace FFmpeg;

public unsafe sealed class FFFrame : CountedReference<AVFrame>
{
    public FFFrame([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(ffmpeg.av_frame_alloc(), filePath, lineNumber)
    {
        // placeholder
    }

    [Obsolete("use AV_CODEC_FLAG_COPY_OPAQUE to pass through arbitrary user data from packets to frames")]
    public long PacketPosition => Reference->pkt_pos;

    public AVRational SampleAspectRatio
    {
        get => Reference->sample_aspect_ratio;
        set => Reference->sample_aspect_ratio = value;
    }

    public AVSampleFormat SampleFormat => (AVSampleFormat)Reference->format;

    public string SampleFormatName => AudioParams.GetSampleFormatName(SampleFormat);

    public AVPixelFormat PixelFormat => (AVPixelFormat)Reference->format;

    public int_array8 LineSize => Reference->linesize;

    public int_array8 PixelStride => LineSize;

    public byte_ptrArray8 Data => Reference->data;

    public int Width => Reference->width;

    public int Height => Reference->height;

    public int SampleCount => Reference->nb_samples;

    public int Channels => Reference->ch_layout.nb_channels;

    public int SampleRate => Reference->sample_rate;

    public double AudioComputedDuration => (double)SampleCount / SampleRate;

    public long Pts
    {
        get => Reference->pts;
        set => Reference->pts = value;
    }

    public long PacketDts => Reference->pkt_dts;

    public long BestEffortPts => Reference->best_effort_timestamp;

    public byte** ExtendedData
    {
        get => Reference->extended_data;
        set => Reference->extended_data = value;
    }

    public AVChannelLayout ChannelLayout =>
        Reference->ch_layout;

    public int SamplesBufferSize =>
        AudioParams.ComputeSamplesBufferSize(Channels, SampleCount, SampleFormat, true);

    public void Reset()
    {
        if (IsEmpty) return;
        ffmpeg.av_frame_unref(this);
    }

    public void MoveTo(FFFrame? destination)
    {
        NativeArgumentException.ThrowIfNullOrEmpty(destination);
        ffmpeg.av_frame_move_ref(destination, this);
    }

    protected override unsafe void DisposeNative(AVFrame* target) =>
        ffmpeg.av_frame_free(&target);
}
