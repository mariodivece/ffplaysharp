namespace FFmpeg;

public unsafe sealed class FFSubtitleRect : UnmanagedReference<AVSubtitleRect>
{
    public FFSubtitleRect(AVSubtitleRect* pointer)
        : base(pointer)
    {
        // placeholder
    }

    public int X => Pointer->x;

    public int Y => Pointer->y;

    public int W => Pointer->w;

    public int H => Pointer->h;

    public byte_ptrArray4 Data => Pointer->data;

    public int_array4 LineSize => Pointer->linesize;
}
