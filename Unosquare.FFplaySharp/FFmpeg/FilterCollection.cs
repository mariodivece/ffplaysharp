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
        get => new(Parent.Pointer->filters[index]);
        set => Parent.Pointer->filters[index] = value.Pointer;
    }

    public override int Count => Convert.ToInt32(Parent.Pointer->nb_filters);
}
