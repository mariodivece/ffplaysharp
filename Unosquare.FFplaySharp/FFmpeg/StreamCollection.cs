namespace FFmpeg;

public unsafe sealed class StreamCollection : ChildCollection<FFFormatContext, FFStream>
{
    public StreamCollection(FFFormatContext parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFStream this[int index]
    {
        get => new(Parent.Target->streams[index]);
        set => Parent.Target->streams[index] = value.Target;
    }

    public override int Count => Convert.ToInt32(Parent.Target->nb_streams);
}
