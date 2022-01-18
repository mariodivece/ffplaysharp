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

    public long ChannelLayout { get; set; }

    public string ChannelLayoutString => GetChannelLayoutString(ChannelLayout);

    public AVSampleFormat SampleFormat { get; set; }

    public int FrameSize => ComputeSamplesBufferSize(Channels, 1, SampleFormat, true);

    public int BytesPerSecond => ComputeSamplesBufferSize(Channels, SampleRate, SampleFormat, true);

    public int BytesPerSample => ffmpeg.av_get_bytes_per_sample(SampleFormat);

    public string SampleFormatName => GetSampleFormatName(SampleFormat);

    public void ImportFrom(FFFrame frame)
    {
        SampleFormat = frame.SampleFormat;
        SampleRate = frame.SampleRate;
        Channels = frame.Channels;
        ChannelLayout = ValidateChannelLayout(frame.ChannelLayout, frame.Channels);
    }

    public void ImportFrom(FFCodecContext codecContext)
    {
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

    public bool IsDifferentTo(FFFrame audioFrame) =>
        AreDifferent(SampleFormat, Channels, audioFrame.SampleFormat, audioFrame.Channels);

    public static AudioParams FromFilterContext(FFFilterContext filter)
    {
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

    public static string GetChannelLayoutString(long channelLayout) =>
        GetChannelLayoutString(Convert.ToUInt64(channelLayout));

    public static string GetSampleFormatName(AVSampleFormat format) =>
        ffmpeg.av_get_sample_fmt_name(format);

    public static long DefaultChannelLayoutFor(int channelCount) =>
        ffmpeg.av_get_default_channel_layout(channelCount);

    public static long ComputeChannelLayout(FFFrame frame)
    {
        if (frame.IsNull())
            throw new ArgumentNullException(nameof(frame));

        return frame.ChannelLayout != 0 && frame.Channels == ChannelCountFor(frame.ChannelLayout)
            ? frame.ChannelLayout
            : DefaultChannelLayoutFor(frame.Channels);
    }

    public static int ChannelCountFor(long channelLayout) =>
        ffmpeg.av_get_channel_layout_nb_channels(Convert.ToUInt64(channelLayout));

    public static long ValidateChannelLayout(long channelLayout, int channelCount)
    {
        if (channelLayout != 0 && ChannelCountFor(channelLayout) == channelCount)
            return channelLayout;
        else
            return 0;
    }

    public static bool AreDifferent(AVSampleFormat sampleFormatA, long channelCountA, AVSampleFormat sampleFormatB, long channelCountB)
    {
        // If channel count == 1, planar and non-planar formats are the same.
        if (channelCountA == 1 && channelCountB == 1)
            return ffmpeg.av_get_packed_sample_fmt(sampleFormatA) != ffmpeg.av_get_packed_sample_fmt(sampleFormatB);
        else
            return channelCountA != channelCountB || sampleFormatA != sampleFormatB;
    }

    private static unsafe string GetChannelLayoutString(ulong channelLayout)
    {
        const int StringBufferLength = 1024;
        var filterLayoutString = stackalloc byte[StringBufferLength];
        ffmpeg.av_get_channel_layout_string(filterLayoutString, StringBufferLength, -1, channelLayout);
        return Helpers.PtrToString(filterLayoutString);
    }
}
