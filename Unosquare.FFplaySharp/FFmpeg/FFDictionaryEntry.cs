using Unosquare.FFplaySharp.Interop;

namespace FFmpeg;

public unsafe class FFDictionaryEntry : NativeReference<AVDictionaryEntry>
{
    public FFDictionaryEntry(AVDictionaryEntry* target)
        : base(target)
    {
        if (target is null)
            return;

        Key = Helpers.PtrToString(target->key);
        Value = Helpers.PtrToString(target->value);
    }

    public string? Key { get; }

    public new string? Value { get; }
}
