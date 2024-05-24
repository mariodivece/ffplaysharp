namespace FFmpeg.Collections;

public unsafe sealed class SubtitleRectSet : NativeChildSet<FFSubtitle, FFSubtitleRect>
{
    public SubtitleRectSet(FFSubtitle parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFSubtitleRect this[int index]
    {
        get => new(Parent.Reference->rects[index]);
        set => Parent.Reference->rects[index] = value.IsValid() ? value.Reference : default;
    }

    public override int Count =>
        Convert.ToInt32(Parent.Reference->num_rects);
}
