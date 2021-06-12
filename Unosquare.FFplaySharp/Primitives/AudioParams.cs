namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using System;

    public unsafe class AudioParams
    {
        public int SampleRate { get; set; }
        
        public int Channels { get; set; }
        
        public long Layout { get; set; }

        public string LayoutString => GetChannelLayoutString(Layout);

        public AVSampleFormat SampleFormat { get; set; }

        public int FrameSize => ffmpeg.av_samples_get_buffer_size(null, Channels, 1, SampleFormat, 1);

        public int BytesPerSecond => ffmpeg.av_samples_get_buffer_size(null, Channels, SampleRate, SampleFormat, 1);

        public int BytesPerSample => ffmpeg.av_get_bytes_per_sample(SampleFormat);

        public string SampleFormatName => GetSampleFormatName(SampleFormat);

        public void ImportFrom(FFFrame frame)
        {
            SampleFormat = frame.SampleFormat;
            Channels = frame.Channels;
            Layout = ValidateChannelLayout(frame.ChannelLayout, frame.Channels);
            SampleRate = frame.SampleRate;
        }

        public void ImportFrom(FFCodecContext codecContext)
        {
            SampleRate = codecContext.SampleRate;
            Channels = codecContext.Channels;
            Layout = ValidateChannelLayout(codecContext.ChannelLayout, codecContext.Channels);
            SampleFormat = codecContext.SampleFormat;
        }

        public AudioParams Clone()
        {
            var result = new AudioParams
            {
                Channels = Channels,
                SampleRate = SampleRate,
                Layout = Layout,
                SampleFormat = SampleFormat
            };

            return result;
        }

        public bool IsDifferentTo(FFFrame audioFrame) =>
            AreDifferent(SampleFormat, Channels, audioFrame.SampleFormat, audioFrame.Channels);

        public static AudioParams FromFilterContext(FFFilterContext filter)
        {
            var result = new AudioParams
            {
                SampleRate = filter.SampleRate,
                Channels = filter.Channels,
                Layout = filter.ChannelLayout,
                SampleFormat = filter.SampleFormat
            };

            return result;
        }

        public static string GetChannelLayoutString(long channelLayout)
            => GetChannelLayoutString(Convert.ToUInt64(channelLayout));

        public static string GetChannelLayoutString(ulong channelLayout)
        {
            const int StringBufferLength = 1024;
            var filterLayoutString = stackalloc byte[StringBufferLength];
            ffmpeg.av_get_channel_layout_string(filterLayoutString, StringBufferLength, -1, channelLayout);
            return Helpers.PtrToString(filterLayoutString);
        }

        public static string GetSampleFormatName(AVSampleFormat format) => ffmpeg.av_get_sample_fmt_name(format);

        public static long DefaultChannelLayoutFor(int channelCount) => ffmpeg.av_get_default_channel_layout(channelCount);

        public static long ComputeChannelLayout(FFFrame frame)
        {
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
    }
}
