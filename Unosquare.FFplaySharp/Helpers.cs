namespace Unosquare.FFplaySharp
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;

    public static class Helpers
    {
        private const string FFmpegDirectory = @"c:\ffmpeg\x64";

        public static void SetFFmpegRootPath(string path = FFmpegDirectory) => ffmpeg.RootPath = path;

        public static bool HasFlag(this int flagsVariable, int flagValue) => (flagsVariable & flagValue) != 0;

        public static int AV_CEIL_RSHIFT(int a, int b) => ((a) + (1 << (b)) - 1) >> (b);


        /// <summary>
        /// Parses a hexagesimal (HOURS:MM:SS.MILLISECONDS) or simple second
        /// and decimal string representing time and returns total microseconds.
        /// </summary>
        /// <param name="timeString">The time represented as a string.</param>
        /// <returns>Total microseconds.</returns>
        public static long ParseTime(this string timeString)
        {
            // HOURS:MM:SS.MILLISECONDS
            var timeStr = timeString.Trim();
            var segments = timeStr.Split(':', StringSplitOptions.TrimEntries).Reverse().ToArray();
            var spanParts = (Hours: 0M, Minutes: 0M, Seconds: 0M);
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                var value = decimal.TryParse(segments[segmentIndex], out var parsedValue) ? parsedValue : 0;
                switch (segmentIndex)
                {
                    case 0:
                        spanParts.Seconds = value;
                        break;
                    case 1:
                        spanParts.Minutes = value;
                        break;
                    case 2:
                        spanParts.Hours = value;
                        break;
                    default:
                        break;
                }
            }

            var isNegative = spanParts.Seconds < 0 || spanParts.Hours < 0;
            var secondsSum = Math.Abs(spanParts.Hours * 60 * 60)
                + Math.Abs(spanParts.Minutes * 60)
                + Math.Abs(spanParts.Seconds);

            var totalSeconds = secondsSum * (isNegative ? -1M : 1M);
            return Convert.ToInt64(totalSeconds) * ffmpeg.AV_TIME_BASE;
        }


        public static int Clamp(this int number, int min, int max) => number < min ? min : number > max ? max : number;

        public static double ToFactor(this AVRational r) => ffmpeg.av_q2d(r);

        public static double ToDouble(this int m) => Convert.ToDouble(m);

        public static double ComputeHypotenuse(double s1, double s2) => Math.Sqrt((s1 * s1) + (s2 * s2));

        /// <summary>
        /// Port of av_display_rotation_get.
        /// </summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        public static unsafe double ComputeMatrixRotation(int* matrix)
        {
            var scale = new double[2];
            scale[0] = ComputeHypotenuse(matrix[0].ToDouble(), matrix[3].ToDouble());
            scale[1] = ComputeHypotenuse(matrix[1].ToDouble(), matrix[4].ToDouble());

            if (scale[0] == 0.0 || scale[1] == 0.0)
                return double.NaN;

            var rotation = Math.Atan2(matrix[1].ToDouble() / scale[1], matrix[0].ToDouble() / scale[0]) * 180 / Math.PI;

            return -rotation;
        }

        /// <summary>
        /// Port of get_rotation
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static unsafe double ComputeDisplayRotation(AVStream* stream)
        {
            var displayMatrix = ffmpeg.av_stream_get_side_data(stream, AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX, null);
            var theta = displayMatrix != null ? -ComputeMatrixRotation((int*)displayMatrix) : 0d;
            theta -= 360 * Math.Floor(theta / 360 + 0.9 / 360);

            if (Math.Abs(theta - 90 * Math.Round(theta / 90, 0)) > 2)
                LogWarning("Odd rotation angle.\n" +
                    "If you want to help, upload a sample " +
                    "of this file to https://streams.videolan.org/upload/ " +
                    "and contact the ffmpeg-devel mailing list. (ffmpeg-devel@ffmpeg.org)");

            return theta;
        }

        public static unsafe int av_opt_set_int_list(void* obj, string name, int[] val, int flags)
        {
            var pinnedValues = stackalloc int[val.Length];
            for (var i = 0; i < val.Length; i++)
                pinnedValues[i] = val[i];

            return ffmpeg.av_opt_set_bin(obj, name, (byte*)pinnedValues, val.Length * sizeof(int), flags);
        }

        public static unsafe int av_opt_set_int_list(void* obj, string name, long[] val, int flags)
        {
            var pinnedValues = stackalloc long[val.Length];
            for (var i = 0; i < val.Length; i++)
                pinnedValues[i] = val[i];

            return ffmpeg.av_opt_set_bin(obj, name, (byte*)pinnedValues, val.Length * sizeof(long), flags);
        }

        public static unsafe byte* strchr(byte* str, char search)
        {
            var byteSearch = Convert.ToByte(search);
            var ptr = str;
            while (true)
            {
                if (*ptr == byteSearch)
                    return ptr;

                if (*ptr == 0)
                    return null;
            }
        }

        /// <summary>
        /// Port of check_stream_specifier
        /// </summary>
        /// <param name="formatContext">The format context.</param>
        /// <param name="stream">The associated stream.</param>
        /// <param name="specifier">The specifier string.</param>
        /// <returns>A non-negative number on success. A negative error code on failure.</returns>
        public static unsafe int CheckStreamSpecifier(FormatContext formatContext, AVStream* stream, string specifier)
        {
            var resultCode = ffmpeg.avformat_match_stream_specifier(formatContext.Pointer, stream, specifier);
            if (resultCode < 0)
                Log(formatContext.Pointer, ffmpeg.AV_LOG_ERROR, $"Invalid stream specifier: {specifier}.\n");

            return resultCode;
        }

        public static unsafe string PtrToString(byte* ptr) => PtrToString((IntPtr)ptr);

        public static unsafe string PtrToString(IntPtr ptr) => Marshal.PtrToStringUTF8(ptr);

        /// <summary>
        /// Port of filter_codec_opts.
        /// </summary>
        /// <param name="opts"></param>
        /// <param name="codec_id"></param>
        /// <param name="s"></param>
        /// <param name="st"></param>
        /// <param name="codec"></param>
        /// <returns></returns>
        public static unsafe FFDictionary FilterCodecOptions(StringDictionary opts, AVCodecID codec_id,
                                    FormatContext s, AVStream* st, AVCodec* codec)
        {

            var filteredOptions = new FFDictionary();

            int flags = s.Pointer->oformat != null ? ffmpeg.AV_OPT_FLAG_ENCODING_PARAM : ffmpeg.AV_OPT_FLAG_DECODING_PARAM;
            if (codec == null)
                codec = s.Pointer->oformat != null ? ffmpeg.avcodec_find_encoder(codec_id) : ffmpeg.avcodec_find_decoder(codec_id);

            // -codec:a:1 ac3
            // option:mediatype:streamindex value
            // option:mediatype
            // option

            var prefix = string.Empty;
            switch (st->codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    prefix = "v";
                    flags |= ffmpeg.AV_OPT_FLAG_VIDEO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    prefix = "a";
                    flags |= ffmpeg.AV_OPT_FLAG_AUDIO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    prefix = "s";
                    flags |= ffmpeg.AV_OPT_FLAG_SUBTITLE_PARAM;
                    break;
            }

            foreach (var t in opts)
            {
                var keyParts = t.Key.Split(':', 2);
                var optionName = keyParts[0];
                var specifier = keyParts.Length > 1 ? keyParts[1] : null;

                var checkResult = specifier != null ? CheckStreamSpecifier(s, st, specifier) : -1;
                if (checkResult <= 0)
                    continue;

                if (MediaClass.Codec.FindOption(optionName, flags, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ) != null ||
                    codec == null ||
                    (codec->priv_class != null &&
                     MediaClass.FromPrivateClass(codec->priv_class).FindOption(optionName, flags, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ) != null))
                    filteredOptions[optionName] = t.Value;
                else if (prefix != string.Empty && optionName.StartsWith(prefix) && optionName.Length > 1 &&
                         MediaClass.Codec.FindOption(optionName.Substring(1), flags, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ) != null)
                    filteredOptions[optionName.Substring(1)] = t.Value;
            }

            return filteredOptions;
        }



        public static unsafe void Log(void* opaque, int logLevel, string message) =>
            ffmpeg.av_log(opaque, logLevel, message);

        public static unsafe void LogError(string message) =>
            Log(null, ffmpeg.AV_LOG_ERROR, message);

        public static unsafe void LogError(AVCodecContext* context, string message) =>
            Log(context, ffmpeg.AV_LOG_ERROR, message);

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

        /// <summary>
        /// Port of print_error. Gets a string representation of an FFmpeg error code.
        /// </summary>
        /// <param name="errorCode">The FFmpeg error code.</param>
        /// <returns>The text representation of the rror code.</returns>
        public static unsafe string print_error(int errorCode)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(errorCode, buffer, (ulong)bufferSize);
            var message = PtrToString(buffer);
            return message;
        }

        /// <summary>
        /// Port of setup_find_stream_info_opts.
        /// Gets an array of dictionaries, each associated with a stream, and unsed for calling
        /// <see cref="ffmpeg.avformat_find_stream_info(AVFormatContext*, AVDictionary**)"/>.
        /// </summary>
        /// <param name="s"></param>
        /// <param name="codecOptions"></param>
        /// <returns></returns>
        public static unsafe IReadOnlyList<FFDictionary> FindStreamInfoOptions(FormatContext s, StringDictionary codecOptions)
        {
            var result = new List<FFDictionary>(s.StreamCount);
            if (s.StreamCount == 0)
                return null;

            for (var i = 0; i < s.StreamCount; i++)
            {
                var streamOptions = FilterCodecOptions(codecOptions, s.Pointer->streams[i]->codecpar->codec_id, s, s.Pointer->streams[i], null);
                result.Add(streamOptions);
            }

            return result;
        }

        public static bool IsValidPts(this long pts) => pts != ffmpeg.AV_NOPTS_VALUE;

        public static bool IsAuto(this int x) => x < 0;

        public static bool IsAuto(this ThreeState x) => ((int)x).IsAuto();

        public static ThreeState ToThreeState(this int x) => x < 0 ? ThreeState.Auto : x > 0 ? ThreeState.On : ThreeState.Off;

        public static bool IsFalse(this int x) => x == 0;

        public static bool IsNaN(this double x) => double.IsNaN(x);

        public static bool IsNull(this IntPtr ptr) => ptr == IntPtr.Zero;
    }
}
