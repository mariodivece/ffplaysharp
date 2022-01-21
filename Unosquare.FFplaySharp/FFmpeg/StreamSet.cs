namespace FFmpeg;

public unsafe sealed class StreamSet : NativeChildSet<FFFormatContext, FFStream>
{
    public StreamSet(FFFormatContext parent)
        : base(parent)
    {
        // placeholder
    }

    public override FFStream this[int index]
    {
        get => new(Parent.Target->streams[index], Parent);
        set => Parent.Target->streams[index] = value.Target;
    }

    public override int Count => Convert.ToInt32(Parent.Target->nb_streams);
}
