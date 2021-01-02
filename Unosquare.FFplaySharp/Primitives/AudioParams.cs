namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;

    public unsafe class AudioParams
    {
        public int Frequency { get; set; }
        public int Channels { get; set; }
        public long Layout { get; set; }
        public AVSampleFormat SampleFormat { get; set; }
        public int FrameSize => ffmpeg.av_samples_get_buffer_size(null, Channels, 1, SampleFormat, 1);

        public int BytesPerSecond => ffmpeg.av_samples_get_buffer_size(null, Channels, Frequency, SampleFormat, 1);

        public int BytesPerSample => ffmpeg.av_get_bytes_per_sample(SampleFormat);

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

        public static long DefaultChannelLayoutFor(int channelCount) => ffmpeg.av_get_default_channel_layout(channelCount);

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

        public bool IsDifferent(AVFrame* audioFrame) =>
            AreDifferent(SampleFormat, Channels, (AVSampleFormat)audioFrame->format, audioFrame->channels);
    }
}
