namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using System;

    public unsafe class AudioParams
    {
        public int Frequency { get; set; }
        
        public int Channels { get; set; }
        
        public long Layout { get; set; }

        public string LayoutString => GetChannelLayoutString(Layout);

        public AVSampleFormat SampleFormat { get; set; }

        public int FrameSize => ffmpeg.av_samples_get_buffer_size(null, Channels, 1, SampleFormat, 1);

        public int BytesPerSecond => ffmpeg.av_samples_get_buffer_size(null, Channels, Frequency, SampleFormat, 1);

        public int BytesPerSample => ffmpeg.av_get_bytes_per_sample(SampleFormat);

        public string SampleFormatName => GetSampleFormatName(SampleFormat);

        public void ImportFrom(AVFrame* frame)
        {
            SampleFormat = (AVSampleFormat)frame->format;
            Channels = frame->channels;
            Layout = ValidateChannelLayout(frame->channel_layout, frame->channels);
            Frequency = frame->sample_rate;
        }

        public void ImportFrom(FFCodecContext codecContext)
        {
            Frequency = codecContext.SampleRate;
            Channels = codecContext.Channels;
            Layout = ValidateChannelLayout(codecContext.ChannelLayout, codecContext.Channels);
            SampleFormat = codecContext.SampleFormat;
        }

        public AudioParams Clone()
        {
            var result = new AudioParams
            {
                Channels = Channels,
                Frequency = Frequency,
                Layout = Layout,
                SampleFormat = SampleFormat
            };

            return result;
        }

        public bool IsDifferentTo(AVFrame* audioFrame) =>
            AreDifferent(SampleFormat, Channels, (AVSampleFormat)audioFrame->format, audioFrame->channels);

        public static AudioParams FromFilterContext(AVFilterContext* filter)
        {
            var result = new AudioParams
            {
                Frequency = ffmpeg.av_buffersink_get_sample_rate(filter),
                Channels = ffmpeg.av_buffersink_get_channels(filter),
                Layout = (long)ffmpeg.av_buffersink_get_channel_layout(filter),
                SampleFormat = (AVSampleFormat)ffmpeg.av_buffersink_get_format(filter)
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

        public static string GetSampleFormatName(int format) => GetSampleFormatName((AVSampleFormat)format);

        public static long DefaultChannelLayoutFor(int channelCount) => ffmpeg.av_get_default_channel_layout(channelCount);

        public static long ComputeChannelLayout(AVFrame* frame)
        {
            return frame->channel_layout != 0 && frame->channels == AudioParams.ChannelCountFor(frame->channel_layout)
                ? (long)frame->channel_layout
                : DefaultChannelLayoutFor(frame->channels);
        }

        public static int ChannelCountFor(ulong channelLayout) => ffmpeg.av_get_channel_layout_nb_channels(channelLayout);

        public static int ChannelCountFor(long channelLayout) => ChannelCountFor((ulong)channelLayout);

        public static long ValidateChannelLayout(ulong channelLayout, int channelCount)
        {
            if (channelLayout != 0 && AudioParams.ChannelCountFor(channelLayout) == channelCount)
                return (long)channelLayout;
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
