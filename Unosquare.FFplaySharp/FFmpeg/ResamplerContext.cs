namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading.Tasks;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class ResamplerContext : UnmanagedCountedReference<SwrContext>
    {
        public ResamplerContext([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.swr_alloc());
        }

        public ResamplerContext(
            long outLayout,
            AVSampleFormat outFormat,
            int outSampleRate,
            long inLayout,
            AVSampleFormat inFormat,
            int inSampleRate,
            [CallerFilePath] string filePath = default,
            [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            var pointer = ffmpeg.swr_alloc_set_opts(null,
                    outLayout, outFormat, outSampleRate,
                    inLayout, inFormat, inSampleRate,
                    0, null);

            Update(pointer);
        }

        public int Convert(byte** output, int outputCount, byte** input, int inputCount) =>
            ffmpeg.swr_convert(Pointer, output, outputCount, input, inputCount);

        public int SetCompensation(int delta, int distance) =>
            ffmpeg.swr_set_compensation(Pointer, delta, distance);

        public int Initialize() =>
            ffmpeg.swr_init(Pointer);

        public int SetOption(string key, string value) =>
            ffmpeg.av_opt_set(Pointer, key, value, 0);

        protected override unsafe void ReleaseInternal(SwrContext* pointer) =>
            ffmpeg.swr_free(&pointer);
    }
}
