namespace FFmpeg
{
    using System;

    public unsafe sealed class StreamCollection : ChildCollection<FFFormatContext, FFStream>
    {
        public StreamCollection(FFFormatContext parent)
            : base(parent)
        {
            // placeholder
        }

        public override FFStream this[int index]
        {
            get => new(Parent.Pointer->streams[index]);
            set => Parent.Pointer->streams[index] = value.Pointer;
        }

        public override int Count => Convert.ToInt32(Parent.Pointer->nb_streams);
    }
}
