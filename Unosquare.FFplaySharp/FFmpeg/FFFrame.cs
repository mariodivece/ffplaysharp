namespace FFmpeg;

public unsafe sealed class FFFrame : CountedReference<AVFrame>
{
    public FFFrame([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        Update(ffmpeg.av_frame_alloc());
    }

    public long PacketPosition => Target->pkt_pos;

    public AVRational SampleAspectRatio
    {
        get => Target->sample_aspect_ratio;
        set => Target->sample_aspect_ratio = value;
    }

    public AVSampleFormat SampleFormat => (AVSampleFormat)Target->format;

    public string SampleFormatName => AudioParams.GetSampleFormatName(SampleFormat);

    public AVPixelFormat PixelFormat => (AVPixelFormat)Target->format;

    public int_array8 LineSize => Target->linesize;

    public int_array8 PixelStride => LineSize;

    public byte_ptrArray8 Data => Target->data;

    public int Width => Target->width;

    public int Height => Target->height;

    public int SampleCount => Target->nb_samples;

    public int Channels => Target->channels;

    public int SampleRate => Target->sample_rate;

    public double AudioComputedDuration => (double)SampleCount / SampleRate;

    public long Pts
    {
        get => Target->pts;
        set => Target->pts = value;
    }

    public long PacketDts => Target->pkt_dts;

    public long BestEffortPts => Target->best_effort_timestamp;

    public byte** ExtendedData
    {
        get => Target->extended_data;
        set => Target->extended_data = value;
    }

    public long ChannelLayout =>
        Convert.ToInt64(Target->channel_layout);

    public int SamplesBufferSize =>
        AudioParams.ComputeSamplesBufferSize(Channels, SampleCount, SampleFormat, true);

    public void Reset()
    {
        if (Address.IsNull())
            return;

        ffmpeg.av_frame_unref(Target);
    }

    public void MoveTo(FFFrame? destination)
    {
        if (destination.IsNull())
            throw new ArgumentNullException(nameof(destination));

        ffmpeg.av_frame_move_ref(destination!.Target, Target);
    }

    protected override unsafe void ReleaseInternal(AVFrame* pointer) =>
        ffmpeg.av_frame_free(&pointer);
}
