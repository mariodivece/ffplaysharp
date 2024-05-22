using Unosquare.FFplaySharp.Interop;

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

    public int Flags => Reference->flags;

    public IReadOnlyList<string> ShortNames => Reference is null
        ? []
        : Helpers.PtrToString(Reference->name)!.Split(',', SplitOptions);

    public static FFInputFormat Find(string shortName)
    {
        var pointer = ffmpeg.av_find_input_format(shortName);

        if (pointer is null)
            throw new ArgumentException($"Could not find input format '{shortName}'", nameof(shortName));

        return new(pointer);
    }
}
