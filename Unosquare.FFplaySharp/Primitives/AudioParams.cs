namespace Unosquare.FFplaySharp.Primitives
{
    using FFmpeg.AutoGen;

    public class AudioParams
    {
        public int Frequency { get; set; }
        public int Channels { get; set; }
        public long Layout { get; set; }
        public AVSampleFormat SampleFormat { get; set; }
        public int FrameSize { get; set; }
        public int BytesPerSecond { get; set; }
    }
}
