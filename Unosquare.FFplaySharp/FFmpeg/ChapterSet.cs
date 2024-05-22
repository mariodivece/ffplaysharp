namespace FFmpeg;

public unsafe sealed class ChapterSet : NativeChildSet<FFFormatContext, FFChapter>
{
    public ChapterSet(FFFormatContext parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFChapter this[int index]
    {
        get => new(Parent.Reference->chapters[index]);
        set => Parent.Reference->chapters[index] = value.Reference;
    }

    public override int Count => Convert.ToInt32(Parent.Reference->nb_chapters);
}
