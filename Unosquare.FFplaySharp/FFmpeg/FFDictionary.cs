namespace FFmpeg;

public unsafe class FFDictionary : CountedReference<AVDictionary>
{
    public FFDictionary([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        // We don't count the reference yet until the pointer is not null.
        ReferenceCounter.Remove(this);
    }

    public FFDictionaryEntry? First => IsEmpty ? default : FirstEntry(this);

    public string? this[string key]
    {
        get => Find(key)?.Value;
        set => Set(key, value);
    }

    public static StringDictionary Extract(AVDictionary* dictionary)
    {
        var result = new StringDictionary();
        FFDictionaryEntry? entry = default;
        while ((entry = NextEntry(dictionary, entry)).IsNotNull())
            result[entry!.Key] = entry.Value;

        return result;
    }

    public void Set(string key, string? value, int flags)
    {
        var wasNull = IsEmpty;
        var pointer = Reference;
        pointer = SetEntry(pointer, key, value, flags);
        UpdatePointer(pointer);

        if (wasNull && Address.IsNotNull())
            ObjectId = ReferenceCounter.Add(this, Source);
        else if (!wasNull && IsEmpty)
            ReferenceCounter.Remove(this);
    }

    public void Set(string key, string? value) =>
        Set(key, value, 0);

    public void Remove(string key) =>
        Set(key, default);

    public FFDictionaryEntry? Next(FFDictionaryEntry? previous) =>
        NextEntry(this, previous);

    public FFDictionaryEntry? Find(string key, bool matchCase = false) =>
        FindEntry(this, key, matchCase);

    public bool ContainsKey(string key, bool matchCase = false) =>
        Find(key, matchCase).IsNotNull();

    public Dictionary<string, string> ToDictionary() =>
        Extract(this);

    public static FFDictionary FromManaged(Dictionary<string, string> other,
        [CallerFilePath] string? filePath = default,
        [CallerLineNumber] int? lineNumber = default)
    {
        ArgumentNullException.ThrowIfNull(other);
        var result = new FFDictionary(filePath, lineNumber);
        foreach (var kvp in other)
            result[kvp.Key] = kvp.Value;

        return result;
    }

    protected override void ReleaseInternal(AVDictionary* target) =>
         ffmpeg.av_dict_free(&target);

    private static FFDictionaryEntry? FirstEntry(AVDictionary* dictionary)
    {
        return NextEntry(dictionary, default);
    }

    private static FFDictionaryEntry? NextEntry(AVDictionary* dictionary, FFDictionaryEntry? previousEntry)
    {
        if (dictionary is null)
            return default;

        var previous = previousEntry.IsNotNull() ? previousEntry!.Reference : default;
        var entry = ffmpeg.av_dict_get(dictionary, string.Empty, previous, ffmpeg.AV_DICT_IGNORE_SUFFIX);
        return entry is not null ? new(entry) : default;
    }

    private static FFDictionaryEntry? FindEntry(AVDictionary* dictionary, string key, bool matchCase = false)
    {
        if (dictionary is null)
            return default;

        var flags = matchCase ? ffmpeg.AV_DICT_MATCH_CASE : 0;
        var entry = ffmpeg.av_dict_get(dictionary, key, null, flags);
        return entry is not null ? new(entry) : default;
    }

    private static AVDictionary* SetEntry(AVDictionary* dictionary, string key, string? value, int flags)
    {
        var resultCode = ffmpeg.av_dict_set(&dictionary, key, value, flags);
        if (resultCode < 0)
            throw new FFmpegException(resultCode);

        return dictionary;
    }
}
