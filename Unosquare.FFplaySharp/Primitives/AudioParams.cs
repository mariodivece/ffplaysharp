namespace Unosquare.FFplaySharp.Primitives;

public class AudioParams
{
    public AudioParams()
    {
        // placeholder
    }

    public int BufferSize { get; set; } = -1;

    public int SampleRate { get; set; }

    public int Channels { get; set; }

    public AVChannelLayout ChannelLayout { get; set; }

    public string ChannelLayoutString => GetChannelLayoutString(ChannelLayout);

    public AVSampleFormat SampleFormat { get; set; }

    public int FrameSize => ComputeSamplesBufferSize(Channels, 1, SampleFormat, true);

    public int BytesPerSecond => ComputeSamplesBufferSize(Channels, SampleRate, SampleFormat, true);

    public int BytesPerSample => ffmpeg.av_get_bytes_per_sample(SampleFormat);

    public string SampleFormatName => GetSampleFormatName(SampleFormat);

    public void ImportFrom(FFFrame frame)
    {
        if (frame is null || frame.IsVoid())
            throw new ArgumentNullException(nameof(frame));

        SampleFormat = frame.SampleFormat;
        SampleRate = frame.SampleRate;
        Channels = frame.Channels;
        ChannelLayout = ValidateChannelLayout(frame.ChannelLayout, frame.Channels);
    }

    public void ImportFrom(FFCodecContext codecContext)
    {
        if (codecContext is null || codecContext.IsVoid())
            throw new ArgumentNullException(nameof(codecContext));

        SampleFormat = codecContext.SampleFormat;
        SampleRate = codecContext.SampleRate;
        Channels = codecContext.Channels;
        ChannelLayout = ValidateChannelLayout(codecContext.ChannelLayout, codecContext.Channels);
    }

    public AudioParams Clone()
    {
        return new()
        {
            Channels = Channels,
            SampleRate = SampleRate,
            ChannelLayout = ChannelLayout,
            SampleFormat = SampleFormat,
            BufferSize = BufferSize
        };
    }

    public bool IsDifferentTo(FFFrame audioFrame) => audioFrame is null || audioFrame.IsVoid()
        ? throw new ArgumentNullException(nameof(audioFrame))
        : AreDifferent(SampleFormat, Channels, audioFrame.SampleFormat, audioFrame.Channels);

    public static AudioParams FromFilterContext(FFFilterContext filter)
    {
        if (filter is null || filter.IsVoid())
            throw new ArgumentNullException(nameof(filter));

        var result = new AudioParams
        {
            SampleRate = filter.SampleRate,
            Channels = filter.Channels,
            ChannelLayout = filter.ChannelLayout,
            SampleFormat = filter.SampleFormat
        };

        return result;
    }

    public static unsafe int ComputeSamplesBufferSize(int channels, int sampleRate, AVSampleFormat sampleFormat, bool align) =>
        ffmpeg.av_samples_get_buffer_size(null, channels, sampleRate, sampleFormat, (align ? 1 : 0));

    public static unsafe string GetChannelLayoutString(AVChannelLayout channelLayout)
    {
        const int StringBufferLength = 1024;
        var filterLayoutString = stackalloc byte[StringBufferLength];
        ffmpeg.av_channel_layout_describe(&channelLayout, filterLayoutString, StringBufferLength);
        return Helpers.PtrToString(filterLayoutString) ?? string.Empty;
    }

    public static string GetSampleFormatName(AVSampleFormat format) =>
        ffmpeg.av_get_sample_fmt_name(format);

    public static unsafe AVChannelLayout DefaultChannelLayoutFor(int channelCount)
    {
        var target = default(AVChannelLayout);
        ffmpeg.av_channel_layout_default(&target, channelCount);
        return target;
    }

    public static AVChannelLayout ComputeChannelLayout(FFFrame frame)
    {
        if (frame is null || frame.IsVoid())
            throw new ArgumentNullException(nameof(frame));

        return frame.ChannelLayout.nb_channels > 0 && frame.Channels == ChannelCountFor(frame.ChannelLayout)
            ? frame.ChannelLayout
            : DefaultChannelLayoutFor(frame.Channels);
    }

    public static int ChannelCountFor(AVChannelLayout channelLayout) => channelLayout.nb_channels;

    public static AVChannelLayout ValidateChannelLayout(AVChannelLayout channelLayout, int channelCount)
    {
        if (channelLayout.nb_channels > 0 && ChannelCountFor(channelLayout) == channelCount)
            return channelLayout;
        else
            return default;
    }

    public static bool AreDifferent(AVSampleFormat sampleFormatA, long channelCountA, AVSampleFormat sampleFormatB, long channelCountB)
    {
        // If channel count == 1, planar and non-planar formats are the same.
        if (channelCountA == 1 && channelCountB == 1)
            return ffmpeg.av_get_packed_sample_fmt(sampleFormatA) != ffmpeg.av_get_packed_sample_fmt(sampleFormatB);
        else
            return channelCountA != channelCountB || sampleFormatA != sampleFormatB;
    }
}
