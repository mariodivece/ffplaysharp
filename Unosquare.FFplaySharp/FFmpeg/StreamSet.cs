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
        get => new(Parent.Reference->streams[index], Parent);
        set => Parent.Reference->streams[index] = value.Reference;
    }

    public override int Count => Convert.ToInt32(Parent.Reference->nb_streams);
}
