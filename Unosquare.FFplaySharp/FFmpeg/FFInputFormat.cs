namespace FFmpeg;

public unsafe sealed class FFInputFormat : NativeReference<AVInputFormat>
{
    private const StringSplitOptions SplitOptions = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

    public FFInputFormat()
    {
        // placeholder
    }

    public FFInputFormat(AVInputFormat* target)
        : base(target)
    {
        // placeholder
    }

    public int Flags => Target->flags;

    public IReadOnlyList<string> ShortNames => Target is null
        ? Array.Empty<string>()
        : Helpers.PtrToString(Target->name)!.Split(',', SplitOptions);

    public static FFInputFormat Find(string shortName)
    {
        var pointer = ffmpeg.av_find_input_format(shortName);

        if (pointer is null)
            throw new ArgumentException($"Could not find input format '{shortName}'", nameof(shortName));

        return new(pointer);
    }
}
