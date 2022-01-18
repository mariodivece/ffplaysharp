namespace FFmpeg;

public unsafe sealed class FFFilterInOut : NativeReference<AVFilterInOut>
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

    public string? Name
    {
        get => Helpers.PtrToString(Target->name);
        set => Target->name = value is not null ? ffmpeg.av_strdup(value) : default;
    }

    public int PadIndex
    {
        get => Target->pad_idx;
        set => Target->pad_idx = value;
    }

    public FFFilterInOut? Next
    {
        get => Address.IsNotNull() && Target->next is not null ? new(Target->next) : default;
        set => Target->next = value.IsNotNull() ? value!.Target : default;
    }

    public FFFilterContext? Filter
    {
        get => Address.IsNotNull() && Target->filter_ctx is not null ? new(Target->filter_ctx) : default;
        set => Target->filter_ctx = value.IsNotNull() ? value!.Target : default;
    }

    public void Release()
    {
        if (Address.IsNotNull())
        {
            var pointer = Target;
            ffmpeg.avfilter_inout_free(&pointer);
        }

        Update(IntPtr.Zero);
    }
}
