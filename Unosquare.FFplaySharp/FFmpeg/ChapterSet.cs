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
        get => new(Parent.Target->chapters[index]);
        set => Parent.Target->chapters[index] = value.Target;
    }

    public override int Count => Convert.ToInt32(Parent.Target->nb_chapters);
}
