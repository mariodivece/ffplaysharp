namespace Unosquare.FFplaySharp
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using System;
    using System.Linq;
    using System.Text;

    public static class Helpers
    {
        private const string FFmpegDirectory = @"c:\ffmpeg\x64";

        public static void SetFFmpegRootPath(string path = FFmpegDirectory) => ffmpeg.RootPath = path;

        public static bool HasFlag(this int flagsVariable, int flagValue) => (flagsVariable & flagValue) != 0;

        public static int AV_CEIL_RSHIFT(int a, int b) => ((a) + (1 << (b)) - 1) >> (b);

        public static unsafe void DumpFormat(FFFormatContext context, string fileName) =>
            ffmpeg.av_dump_format(context.Pointer, 0, fileName, 0);

        /// <summary>
        /// Parses a hexagesimal (HOURS:MM:SS.MILLISECONDS) or simple second
        /// and decimal string representing time and returns total microseconds.
        /// </summary>
        /// <param name="timeString">The time represented as a string.</param>
        /// <returns>Total microseconds.</returns>
        public static long ParseTime(this string timeString)
        {
            var semicolonSeparator = new char[] { ':' };
            // HOURS:MM:SS.MILLISECONDS
            var timeStr = timeString.Trim();
            var segments = timeStr.Split(semicolonSeparator).Select(c => c.Trim()).Reverse().ToArray();
            var (hours, minutes, seconds) = (0M, 0M, 0M);
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                var value = decimal.TryParse(segments[segmentIndex], out var parsedValue) ? parsedValue : 0;
                switch (segmentIndex)
                {
                    case 0:
                        seconds = value;
                        break;
                    case 1:
                        minutes = value;
                        break;
                    case 2:
                        hours = value;
                        break;
                    default:
                        break;
                }
            }

            var isNegative = seconds < 0 || hours < 0;
            var secondsSum = Math.Abs(hours * 60 * 60)
                + Math.Abs(minutes * 60)
                + Math.Abs(seconds);

            var totalSeconds = secondsSum * (isNegative ? -1M : 1M);
            return Convert.ToInt64(totalSeconds) * ffmpeg.AV_TIME_BASE;
        }

        public static int Clamp(this int number, int min, int max) => number < min ? min : number > max ? max : number;

        public static double ToFactor(this AVRational r) => ffmpeg.av_q2d(r);

        public static double ToDouble(this int m) => Convert.ToDouble(m);

        public static bool IsAudio(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_AUDIO;

        public static bool IsVideo(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_VIDEO;

        public static bool IsSubtitle(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

        public static string ToText(this AVMediaType t) => ffmpeg.av_get_media_type_string(t);

        /// <summary>
        /// Port of check_stream_specifier.
        /// Returns 0 for no match, 1 for match and a negative number on error.
        /// </summary>
        /// <param name="formatContext">The format context.</param>
        /// <param name="stream">The associated stream.</param>
        /// <param name="specifier">The specifier string.</param>
        /// <returns>A non-negative number on success. A negative error code on failure.</returns>
        public static unsafe int CheckStreamSpecifier(FFFormatContext formatContext, FFStream stream, string specifier)
        {
            var resultCode = MatchStreamSpecifier(formatContext, stream, specifier);
            if (resultCode < 0)
                Log(formatContext.Pointer, ffmpeg.AV_LOG_ERROR, $"Invalid stream specifier: {specifier}.\n");

            return resultCode;
        }

        public static unsafe int MatchStreamSpecifier(FFFormatContext formatContext, FFStream stream, string specifier) =>
            ffmpeg.avformat_match_stream_specifier(formatContext.Pointer, stream.Pointer, specifier);

        public static unsafe string PtrToString(byte* ptr) => PtrToString((IntPtr)ptr);

        public static unsafe string PtrToString(IntPtr address)
        {
            if (address == IntPtr.Zero)
                return null;

            var source = (byte*)address.ToPointer();
            var length = 0;
            while (source[length] != 0)
                ++length;

            if (length == 0)
                return string.Empty;

            var target = stackalloc byte[length];
            Buffer.MemoryCopy(source, target, length, length);
            return Encoding.UTF8.GetString(target, length);
        } 

        /// <summary>
        /// Port of filter_codec_opts.
        /// </summary>
        /// <param name="allOptions"></param>
        /// <param name="codecId"></param>
        /// <param name="formatContext"></param>
        /// <param name="stream"></param>
        /// <param name="codec"></param>
        /// <returns></returns>
        public static unsafe FFDictionary FilterCodecOptions(
            StringDictionary allOptions,
            AVCodecID codecId,
            FFFormatContext formatContext,
            FFStream stream,
            FFCodec codec)
        {

            var filteredOptions = new FFDictionary();

            int optionFlags = formatContext.Pointer->oformat != null
                ? ffmpeg.AV_OPT_FLAG_ENCODING_PARAM
                : ffmpeg.AV_OPT_FLAG_DECODING_PARAM;

            if (codec == null)
            {
                codec = formatContext.Pointer->oformat != null
                    ? FFCodec.FromEncoderId(codecId)
                    : FFCodec.FromDecoderId(codecId);
            }
                

            // -codec:a:1 ac3
            // option:mediatype:streamindex value
            // option:mediatype
            // option

            var prefix = string.Empty;
            switch (stream.CodecParameters.CodecType)
            {
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    prefix = "v";
                    optionFlags |= ffmpeg.AV_OPT_FLAG_VIDEO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    prefix = "a";
                    optionFlags |= ffmpeg.AV_OPT_FLAG_AUDIO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    prefix = "s";
                    optionFlags |= ffmpeg.AV_OPT_FLAG_SUBTITLE_PARAM;
                    break;
            }

            var semicolonSeprator = new char[] { ':' };
            foreach (var t in allOptions)
            {
                var keyParts = t.Key.Split(semicolonSeprator, 2);
                var optionName = keyParts[0];
                var specifier = keyParts.Length > 1 ? keyParts[1] : null;

                var checkResult = specifier != null
                    ? CheckStreamSpecifier(formatContext, stream, specifier)
                    : -1;

                if (checkResult <= 0)
                    continue;

                if (FFMediaClass.Codec.HasOption(optionName, optionFlags) || codec == null ||
                    codec.PrivateClass.HasOption(optionName, optionFlags))
                {
                    filteredOptions[optionName] = t.Value;
                }
                else if (prefix.Length > 0 && optionName.Length > 1 && optionName.StartsWith(prefix) &&
                    FFMediaClass.Codec.HasOption(optionName.Substring(1), optionFlags))
                {
                    filteredOptions[optionName.Substring(1)] = t.Value;
                }
            }

            return filteredOptions;
        }

        public static unsafe int ComputeSamplesBufferSize(int channels, int sampleRate, AVSampleFormat sampleFormat, bool align) =>
            ffmpeg.av_samples_get_buffer_size(null, channels, sampleRate, sampleFormat, (align ? 1 : 0));

        public static unsafe void Log(void* opaque, int logLevel, string message) =>
            ffmpeg.av_log(opaque, logLevel, message);

        public static unsafe void LogError(string message) =>
            Log(null, ffmpeg.AV_LOG_ERROR, message);

        public static unsafe void LogError(FFCodecContext context, string message) =>
            Log(context.Pointer, ffmpeg.AV_LOG_ERROR, message);

        public static unsafe void LogWarning(string message) =>
            Log(null, ffmpeg.AV_LOG_WARNING, message);

        public static unsafe void LogWarning(AVCodecContext* context, string message) =>
            Log(context, ffmpeg.AV_LOG_WARNING, message);

        public static unsafe void LogFatal(string message) =>
            Log(null, ffmpeg.AV_LOG_FATAL, message);

        public static unsafe void LogDebug(string message) =>
            Log(null, ffmpeg.AV_LOG_DEBUG, message);

        public static unsafe void LogInfo(string message) =>
            Log(null, ffmpeg.AV_LOG_INFO, message);

        public static unsafe void LogVerbose(string message) =>
            Log(null, ffmpeg.AV_LOG_VERBOSE, message);

        public static unsafe void LogTrace(string message) =>
            Log(null, ffmpeg.AV_LOG_TRACE, message);

        public static unsafe void LogQuiet(string message) =>
            Log(null, ffmpeg.AV_LOG_QUIET, message);

        public static bool IsValidPts(this long pts) => pts != ffmpeg.AV_NOPTS_VALUE;

        public static bool IsAuto(this int x) => x < 0;

        public static bool IsAuto(this ThreeState x) => ((int)x).IsAuto();

        public static ThreeState ToThreeState(this int x) => x < 0 ? ThreeState.Auto : x > 0 ? ThreeState.On : ThreeState.Off;

        public static bool IsFalse(this int x) => x == 0;

        public static bool IsNaN(this double x) => double.IsNaN(x);

        public static bool IsNull(this IntPtr ptr) => ptr == IntPtr.Zero;
    }
}
