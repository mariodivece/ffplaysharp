namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class Stream : UnmanagedReference<AVStream>
    {
        public Stream(AVStream* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public AVDiscard DiscardFlags
        {
            get => Pointer->discard;
            set => Pointer->discard = value;
        }

        public AVRational TimeBase => Pointer->time_base;

        public long StartTime => Pointer->start_time;
    }

    public unsafe sealed class StreamCollection
    {
        private readonly FormatContext Parent;

        public StreamCollection(FormatContext parent)
        {
            Parent = parent;
        }

        public Stream this[int index]
        {
            get => new(Parent.Pointer->streams[index]);
        }

        public int Count => Convert.ToInt32(Parent.Pointer->nb_streams);
    }
}
