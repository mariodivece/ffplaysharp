namespace FFmpeg
{
    using System;

    public unsafe sealed class ChapterCollection
    {
        private readonly FFFormatContext Parent;

        public ChapterCollection(FFFormatContext parent)
        {
            Parent = parent;
        }

        public FFChapter this[int index]
        {
            get => new(Parent.Pointer->chapters[index]);
        }

        public int Count => Convert.ToInt32(Parent.Pointer->nb_chapters);
    }
}
