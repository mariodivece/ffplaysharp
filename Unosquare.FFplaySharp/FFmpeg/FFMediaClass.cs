namespace FFmpeg;

public unsafe sealed class FFMediaClass : NativeReference<AVClass>
{
    public static readonly FFMediaClass Codec = new(ffmpeg.avcodec_get_class());
    public static readonly FFMediaClass Format = new(ffmpeg.avformat_get_class());
    public static readonly FFMediaClass Scaler = new(ffmpeg.sws_get_class());
    public static readonly FFMediaClass Resampler = new(ffmpeg.swr_get_class());

    public FFMediaClass(AVClass* target)
        : base(target)
    {
        // placeholder
    }

    /// <summary>
    /// Port of opt_find. Finds an option in a given ffmpeg class.
    /// </summary>
    /// <param name="classObject"></param>
    /// <param name="optionName"></param>
    /// <param name="searchFlags"></param>
    /// <returns></returns>
    public FFOption? FindOption(string optionName, int optionFlags = default, int searchFlags = ffmpeg.AV_OPT_SEARCH_FAKE_OBJ)
    {
        if (IsEmpty || string.IsNullOrWhiteSpace(optionName)) return default;

        var option = ffmpeg.av_opt_find(this, optionName, null, optionFlags, searchFlags);
        return option is not null && option->flags != (int)AVOptionType.AV_OPT_TYPE_FLAGS
            ? new(option)
            : default;
    }

    public bool HasOption(string optionName, int optionFlags = default, int searchFlags = ffmpeg.AV_OPT_SEARCH_FAKE_OBJ) =>
        FindOption(optionName, optionFlags, searchFlags).IsValid();


    public static FFMediaClass FromPrivateClass(AVClass* target) => target is null
        ? throw new ArgumentNullException(nameof(target))
        : new FFMediaClass(target);
}
