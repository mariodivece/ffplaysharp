﻿namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class FFDictionaryEntry : UnmanagedReference<AVDictionaryEntry>
    {
        public FFDictionaryEntry(AVDictionaryEntry* pointer)
            : base(pointer)
        {
            if (pointer == null)
                return;

            Key = Helpers.PtrToString(pointer->key);
            Value = Helpers.PtrToString(pointer->value);
        }

        public string Key { get; }

        public string Value { get; }
    }
}
