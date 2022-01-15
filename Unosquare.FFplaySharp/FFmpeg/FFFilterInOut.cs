namespace FFmpeg;

public unsafe sealed class FFFilterInOut : UnmanagedReference<AVFilterInOut>
{
    public FFFilterInOut()
        : base(ffmpeg.avfilter_inout_alloc())
    {
        // placeholder
    }

    private FFFilterInOut(AVFilterInOut* pointer)
        : base(pointer)
    {
        // placeholder
    }

    public string Name
    {
        get => Helpers.PtrToString(Pointer->name);
        set => Pointer->name = value != null ? ffmpeg.av_strdup(value) : null;
    }

    public int PadIndex
    {
        get => Pointer->pad_idx;
        set => Pointer->pad_idx = value;
    }

    public FFFilterInOut Next
    {
        get => Pointer->next != null ? new(Pointer->next) : null;
        set => Pointer->next = value != null ? value.Pointer : null;
    }

    public FFFilterContext Filter
    {
        get => Pointer->filter_ctx != null ? new(Pointer->filter_ctx) : null;
        set => Pointer->filter_ctx = value != null ? value.Pointer : null;
    }

    public void Release()
    {
        if (!Address.IsNull())
        {
            var pointer = Pointer;
            ffmpeg.avfilter_inout_free(&pointer);
        }

        Update(IntPtr.Zero);
    }
}
