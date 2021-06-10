namespace FFmpeg
{
    using System;

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
