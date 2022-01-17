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

    public FFFilterInOut? Next
    {
        get => Address.IsNotNull() && Pointer->next is not null ? new(Pointer->next) : default;
        set => Pointer->next = value.IsNotNull() ? value!.Pointer : default;
    }

    public FFFilterContext? Filter
    {
        get => Address.IsNotNull() && Pointer->filter_ctx is not null ? new(Pointer->filter_ctx) : default;
        set => Pointer->filter_ctx = value.IsNotNull() ? value!.Pointer : default;
    }

    public void Release()
    {
        if (Address.IsNotNull())
        {
            var pointer = Pointer;
            ffmpeg.avfilter_inout_free(&pointer);
        }

        Update(IntPtr.Zero);
    }
}
