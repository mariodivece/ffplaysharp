namespace FFmpeg
{
    using FFmpeg.AutoGen;

    public static unsafe class FFLog
    {
        public static int Level
        {
            get => ffmpeg.av_log_get_level();
            set => ffmpeg.av_log_set_level(value);
        }

        public static int Flags
        {
            get => ffmpeg.av_log_get_flags();
            set => ffmpeg.av_log_set_flags(value);
        }

        private static void Log(void* opaque, int logLevel, string message, bool addNewLine = true) =>
            ffmpeg.av_log(opaque, logLevel, addNewLine ? $"{message}\n" : message);

        public static void LogError(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_ERROR, message, addNewLine);

        public static void LogError(this FFCodecContext context, string message, bool addNewLine = true) =>
            Log(context.Pointer, ffmpeg.AV_LOG_ERROR, message, addNewLine);

        public static void LogError(this FFFormatContext context, string message, bool addNewLine = true) =>
            Log(context.Pointer, ffmpeg.AV_LOG_ERROR, message, addNewLine);

        public static void LogWarning(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_WARNING, message, addNewLine);

        public static void LogFatal(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_FATAL, message, addNewLine);

        public static void LogDebug(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_DEBUG, message, addNewLine);

        public static void LogInfo(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_INFO, message, addNewLine);

        public static void LogVerbose(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_VERBOSE, message, addNewLine);

        public static void LogTrace(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_TRACE, message, addNewLine);

        public static void LogQuiet(this string message, bool addNewLine = true) =>
            Log(null, ffmpeg.AV_LOG_QUIET, message, addNewLine);
    }
}
