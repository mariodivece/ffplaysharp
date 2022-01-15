namespace FFmpeg;

public unsafe sealed class SubtitleRectCollection : ChildCollection<FFSubtitle, FFSubtitleRect>
{
    public SubtitleRectCollection(FFSubtitle parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFSubtitleRect this[int index]
    {
        get => new(Parent.Pointer->rects[index]);
        set => Parent.Pointer->rects[index] = value != null ? value.Pointer : null;
    }

    public override int Count =>
        Convert.ToInt32(Parent.Pointer->num_rects);
}
