namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;

    public unsafe class MediaComponent
    {
        public PacketQueue Packets { get; } = new();

        public FrameQueue Frames;

        public MediaDecoder Decoder;

        public AVStream* Stream;

        public int StreamIndex;

        public int LastStreamIndex;

        public bool HasEnoughPackets
        {
            get
            {
                return StreamIndex < 0 ||
                   Packets.IsClosed ||
                   (Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0 ||
                   Packets.Count > Constants.MIN_FRAMES && (Packets.Duration == 0 ||
                   ffmpeg.av_q2d(Stream->time_base) * Packets.Duration > 1.0);
            }
        }
    }

    public unsafe class FilteringMediaComponent : MediaComponent
    {
        public AVFilterContext* InputFilter;
        public AVFilterContext* OutputFilter;
    }

    public unsafe class VideoComponent : FilteringMediaComponent
    {
        public SwsContext* ConvertContext;
    }

    public unsafe class AudioComponent : FilteringMediaComponent
    {
        public SwrContext* ConvertContext;

        public AudioParams SourceSpec = new();
        public AudioParams FilterSpec = new();
        public AudioParams TargetSpec = new();
    }

    public unsafe class SubtitleComponent : MediaComponent
    {
        public SwsContext* ConvertContext;
    }
}
