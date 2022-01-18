namespace FFmpeg;

public unsafe sealed class FFSubtitleRect : NativeReference<AVSubtitleRect>
{
    public FFSubtitleRect(AVSubtitleRect* target)
        : base(target)
    {
        // placeholder
    }

    public int X => Target->x;

    public int Y => Target->y;

    public int W => Target->w;

    public int H => Target->h;

    public byte_ptrArray4 Data => Target->data;

    public int_array4 LineSize => Target->linesize;
}
