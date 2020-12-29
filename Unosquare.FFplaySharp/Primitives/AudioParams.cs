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

        public static long DefaultChannelLayoutFor(int channelCount) => ffmpeg.av_get_default_channel_layout(channelCount);

        public static int ChannelCountFor(ulong channelLayout) => ffmpeg.av_get_channel_layout_nb_channels(channelLayout);

        public static int ChannelCountFor(long channelLayout) => ChannelCountFor((ulong)channelLayout);
    }
}
