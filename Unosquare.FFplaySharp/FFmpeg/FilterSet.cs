namespace FFmpeg;

public unsafe sealed class FilterSet : NativeChildSet<FFFilterGraph, FFFilterContext>
{
    public FilterSet(FFFilterGraph parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFFilterContext this[int index]
    {
        get => new(Parent.Target->filters[index]);
        set => Parent.Target->filters[index] = value.Target;
    }

    public override int Count => Convert.ToInt32(Parent.Target->nb_filters);
}
