﻿namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class ByteBuffer : UnmanagedCountedReference<byte>
    {
        public ByteBuffer(ulong length, [CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            var pointer = (byte*)ffmpeg.av_mallocz(length);
            Update(pointer);
            Length = length;
        }

        public ulong Length { get; private set; }

        public static ByteBuffer Reallocate(ByteBuffer original, ulong length)
        {
            if (original == null || original.Length < length)
            {
                original?.Release();
                return new(length);
            }

            return original;
        }

        public void Write(byte* source, int length)
        {
            var maxLength = Math.Min(Convert.ToInt32(Length), length);
            Buffer.MemoryCopy(source, Pointer, maxLength, maxLength);
        }

        protected override void ReleaseInternal(byte* pointer)
        {
            ffmpeg.av_free(pointer);
            Length = 0;
        }
    }
}
