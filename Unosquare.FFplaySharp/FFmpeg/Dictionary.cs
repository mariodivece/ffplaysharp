namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class Dictionary : UnmanagedCountedReference<AVDictionary>
    {
        public Dictionary([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            // placeholder
        }

        public static Dictionary<string, string> Extract(AVDictionary* dictionary)
        {
            var result = new Dictionary<string, string>(64);
            DictionaryEntry entry = null;
            while ((entry = Next(dictionary, entry)) != null)
                result[entry.Key] = entry.Value;
            
            return result;
        }

        public static DictionaryEntry First(AVDictionary* dictionary)
        {
            return Next(dictionary, null);
        }

        public static DictionaryEntry Next(AVDictionary* dictionary, DictionaryEntry previousEntry)
        {
            if (dictionary == null)
                return null;

            var previous = previousEntry != null ? previousEntry.Pointer : null;
            var entry = ffmpeg.av_dict_get(dictionary, string.Empty, previous, ffmpeg.AV_DICT_IGNORE_SUFFIX);
            return entry != null ? new(entry) : null;
        }

        public static DictionaryEntry Find(AVDictionary* dictionary, string key, bool matchCase = false)
        {
            if (dictionary == null)
                return null;

            var flags = matchCase ? ffmpeg.AV_DICT_MATCH_CASE : 0;
            var entry = ffmpeg.av_dict_get(dictionary, key, null, flags);
            return entry != null ? new(entry) : null;
        }

        protected override unsafe void ReleaseInternal(AVDictionary* pointer) =>
             ffmpeg.av_dict_free(&pointer);
    }
}
