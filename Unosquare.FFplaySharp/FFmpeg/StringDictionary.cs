namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;

    public sealed class StringDictionary : Dictionary<string, string>
    {
        public StringDictionary()
            : base(128, StringComparer.InvariantCultureIgnoreCase)
        {
            // placeholder
        }

        public FFDictionary ToUnmanaged() =>
            FFDictionary.FromManaged(this);

        public unsafe void Set(FFOption option, string key, string value)
        {
            var performAppend = option.Type == AVOptionType.AV_OPT_TYPE_FLAGS
                && (value.StartsWith("-") || value.StartsWith("+"));

            this[key] = ContainsKey(key) && performAppend
                ? $"{this[key]}{value}"
                : value;
        }
    }
}
