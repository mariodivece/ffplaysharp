namespace FFmpeg;

public unsafe sealed class ChapterCollection : ChildCollection<FFFormatContext, FFChapter>
{
    public ChapterCollection(FFFormatContext parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFChapter this[int index]
    {
        get => new(Parent.Pointer->chapters[index]);
        set => Parent.Pointer->chapters[index] = value.Pointer;
    }

    public override int Count => Convert.ToInt32(Parent.Pointer->nb_chapters);
}
