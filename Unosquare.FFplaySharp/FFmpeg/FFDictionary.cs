namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class FFDictionary : UnmanagedCountedReference<AVDictionary>
    {
        public FFDictionary([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            // We don't count the reference yet until the pointer is not null.
            ReferenceCounter.Remove(this);
        }

        public FFDictionaryEntry First => FirstEntry(Pointer);

        public string this[string key]
        {
            get => Find(key).Value;
            set => Set(key, value);
        }

        public static StringDictionary Extract(AVDictionary* dictionary)
        {
            var result = new StringDictionary();
            FFDictionaryEntry entry = null;
            while ((entry = NextEntry(dictionary, entry)) != null)
                result[entry.Key] = entry.Value;

            return result;
        }

        public void Set(string key, string value, int flags)
        {
            var wasNull = IsNull;
            var pointer = Pointer;
            pointer = SetEntry(pointer, key, value, flags);
            Update(pointer);

            if (wasNull && !IsNull)
                ObjectId = ReferenceCounter.Add(this, Source);
            else if (!wasNull && IsNull)
                ReferenceCounter.Remove(this);
        }

        public void Set(string key, string value) =>
            Set(key, value, 0);

        public void Remove(string key) =>
            Set(key, null);

        public FFDictionaryEntry Next(FFDictionaryEntry previous) =>
            NextEntry(Pointer, previous);

        public FFDictionaryEntry Find(string key, bool matchCase = false) =>
            FindEntry(Pointer, key, matchCase);

        public bool ContainsKey(string key, bool matchCase = false) =>
            Find(key, matchCase) != null;

        public Dictionary<string, string> ToDictionary() =>
            Extract(Pointer);

        public static FFDictionary FromManaged(Dictionary<string, string> other,
            [CallerFilePath] string filePath = default,
            [CallerLineNumber] int lineNumber = default)
        {
            var result = new FFDictionary(filePath, lineNumber);
            foreach (var kvp in other)
                result[kvp.Key] = kvp.Value;

            return result;
        }

        protected override void ReleaseInternal(AVDictionary* pointer) =>
             ffmpeg.av_dict_free(&pointer);

        private static FFDictionaryEntry FirstEntry(AVDictionary* dictionary)
        {
            return NextEntry(dictionary, null);
        }

        private static FFDictionaryEntry NextEntry(AVDictionary* dictionary, FFDictionaryEntry previousEntry)
        {
            if (dictionary == null)
                return null;

            var previous = previousEntry != null ? previousEntry.Pointer : null;
            var entry = ffmpeg.av_dict_get(dictionary, string.Empty, previous, ffmpeg.AV_DICT_IGNORE_SUFFIX);
            return entry != null ? new(entry) : null;
        }

        private static FFDictionaryEntry FindEntry(AVDictionary* dictionary, string key, bool matchCase = false)
        {
            if (dictionary == null)
                return null;

            var flags = matchCase ? ffmpeg.AV_DICT_MATCH_CASE : 0;
            var entry = ffmpeg.av_dict_get(dictionary, key, null, flags);
            return entry != null ? new(entry) : null;
        }

        private static AVDictionary* SetEntry(AVDictionary* dictionary, string key, string value, int flags)
        {
            var resultCode = ffmpeg.av_dict_set(&dictionary, key, value, flags);
            if (resultCode < 0)
                throw new FFmpegException(resultCode);

            return dictionary;
        }

        private static AVDictionary* SetEntry(AVDictionary* dictionary, string key, string value) =>
            SetEntry(dictionary, key, value, 0);
    }
}
