namespace FFmpeg;

public unsafe class FFDictionaryEntry : UnmanagedReference<AVDictionaryEntry>
{
    public FFDictionaryEntry(AVDictionaryEntry* pointer)
        : base(pointer)
    {
        if (pointer is null)
            return;

        Key = Helpers.PtrToString(pointer->key);
        Value = Helpers.PtrToString(pointer->value);
    }

    public string Key { get; }

    public string Value { get; }
}
