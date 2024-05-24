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
        get => Helpers.PtrToString(Reference->name);
        set => Reference->name = value is not null ? ffmpeg.av_strdup(value) : default;
    }

    public int PadIndex
    {
        get => Reference->pad_idx;
        set => Reference->pad_idx = value;
    }

    public FFFilterInOut? Next
    {
        get => !IsEmpty && Reference->next is not null ? new(Reference->next) : default;
        set => Reference->next = value is not null && value.IsValid() ? value.Reference : default;
    }

    public FFFilterContext? Filter
    {
        get => !IsEmpty && Reference->filter_ctx is not null ? new(Reference->filter_ctx) : default;
        set => Reference->filter_ctx = value is not null && value.IsValid() ? value.Reference : default;
    }

    public void Release()
    {
        if (IsEmpty)
            return;

        using var pointer = AsDoublePointer();
        ffmpeg.avfilter_inout_free(pointer);
    }
}
