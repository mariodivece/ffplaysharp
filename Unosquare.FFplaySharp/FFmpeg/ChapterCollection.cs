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
        get => new(Parent.Target->chapters[index]);
        set => Parent.Target->chapters[index] = value.Target;
    }

    public override int Count => Convert.ToInt32(Parent.Target->nb_chapters);
}
