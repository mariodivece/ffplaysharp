namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFormatContext : UnmanagedCountedReference<AVFormatContext>
    {
        public FFFormatContext([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.avformat_alloc_context());
            Streams = new(this);
        }

        public AVIOInterruptCB_callback_func InterruptCallback
        {
            get => Pointer->interrupt_callback.callback;
            set => Pointer->interrupt_callback.callback = value;
        }

        public StreamCollection Streams { get; }

        public int Flags
        {
            get => Pointer->flags;
            set => Pointer->flags = value;
        }

        public int ChapterCount => Convert.ToInt32(Pointer->nb_chapters);

        public long Duration => Pointer->duration;

        public double DurationSeconds => Duration / Clock.TimeBaseMicros;

        public void InjectGlobalSideData() => ffmpeg.av_format_inject_global_side_data(Pointer);

        public FFProgram FindProgramByStream(int streamIndex)
        {
            var program = ffmpeg.av_find_program_from_stream(Pointer, null, streamIndex);
            return program != null ? new(program) : null;
        }

        public AVRational GuessFrameRate(FFStream stream) => ffmpeg.av_guess_frame_rate(Pointer, stream.Pointer, null);

        public AVRational GuessAspectRatio(FFStream stream, AVFrame* frame) =>
            ffmpeg.av_guess_sample_aspect_ratio(Pointer, stream.Pointer, frame);

        public int OpenInput(string filePath, FFInputFormat format, FFDictionary formatOptions)
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
