namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFStream : UnmanagedReference<AVStream>
    {
        public FFStream(AVStream* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public AVDiscard DiscardFlags
        {
            get => Pointer->discard;
            set => Pointer->discard = value;
        }

        public FFCodecParameters CodecParameters => new(Pointer->codecpar);

        public AVRational TimeBase => Pointer->time_base;

        public long StartTime => Pointer->start_time;
    }

    public unsafe sealed class StreamCollection
    {
        private readonly FFFormatContext Parent;

        public StreamCollection(FFFormatContext parent)
        {
            Parent = parent;
        }

        public FFStream this[int index]
        {
            get => new(Parent.Pointer->streams[index]);
        }

        public int Count => Convert.ToInt32(Parent.Pointer->nb_streams);
    }
}
