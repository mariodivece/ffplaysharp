namespace FFmpeg.Collections;

public unsafe sealed class FilterSet : NativeChildSet<FFFilterGraph, FFFilterContext>
{
    public FilterSet(FFFilterGraph parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFFilterContext this[int index]
    {
        get => new(Parent.Reference->filters[index]);
        set => Parent.Reference->filters[index] = value.Reference;
    }

    public override int Count => Convert.ToInt32(Parent.Reference->nb_filters);
}
