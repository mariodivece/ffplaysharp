namespace FFmpeg;

public unsafe sealed class FilterCollection : ChildCollection<FFFilterGraph, FFFilterContext>
{
    public FilterCollection(FFFilterGraph parent)
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
