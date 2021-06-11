namespace FFmpeg
{
    using System;

    public unsafe sealed class FilterCollection
    {
        private readonly FFFilterGraph Parent;

        public FilterCollection(FFFilterGraph parent)
        {
            Parent = parent;
        }

        public FFFilterContext this[int index]
        {
            get => new(Parent.Pointer->filters[index]);
        }

        public int Count => Convert.ToInt32(Parent.Pointer->nb_filters);
    }
}