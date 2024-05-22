namespace FFmpeg;

public unsafe sealed class FFSubtitleRect : NativeReference<AVSubtitleRect>
{
    public FFSubtitleRect(AVSubtitleRect* target)
        : base(target)
    {
        // placeholder
    }

    public int X => Reference->x;

    public int Y => Reference->y;

    public int W => Reference->w;

    public int H => Reference->h;

    public byte_ptrArray4 Data => Reference->data;

    public int_array4 LineSize => Reference->linesize;
}
