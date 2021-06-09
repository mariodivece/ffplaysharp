namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FormatContext : UnmanagedCountedReference<AVFormatContext>
    {
        public FormatContext([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.avformat_alloc_context());
        }

        public AVIOInterruptCB_callback_func InterruptCallback
        {
            get => Pointer->interrupt_callback.callback;
            set => Pointer->interrupt_callback.callback = value;
        }

        public int OpenInput(string filePath, InputFormat format, FFDictionary formatOptions)
        {
            var context = Pointer;
            var formatOptionsPtr = formatOptions.Pointer;
            var resultCode = ffmpeg.avformat_open_input(&context, filePath, format.Pointer, &formatOptionsPtr);
            Update(context);
            formatOptions.Update(formatOptionsPtr);
            
            return resultCode;
        }

        protected override unsafe void ReleaseInternal(AVFormatContext* pointer) =>
            ffmpeg.avformat_close_input(&pointer);

    }
}
