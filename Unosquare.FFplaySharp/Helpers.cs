namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using Unosquare.FFplaySharp.Primitives;

    public static class Helpers
    {
        private const string Kernel32 = "kernel32";
        private const string FFmpegDirectory = @"c:\ffmpeg\x64";
        private const string SdlDirectory = @"c:\ffmpeg\x64";

        private static readonly IList<string> NativeLibraryPaths = new List<string>()
        {
            Path.Combine(SdlDirectory, "SDL2.dll"),
            Path.Combine(FFmpegDirectory, "avutil-56.dll"),
            Path.Combine(FFmpegDirectory, "swresample-3.dll"),
            Path.Combine(FFmpegDirectory, "swscale-5.dll"),
            Path.Combine(FFmpegDirectory, "avcodec-58.dll"),
            Path.Combine(FFmpegDirectory, "avformat-58.dll"),
            Path.Combine(FFmpegDirectory, "postproc-55.dll"),
            Path.Combine(FFmpegDirectory, "avfilter-7.dll"),
            Path.Combine(FFmpegDirectory, "avdevice-58.dll"),
        };

        private static Dictionary<string, IntPtr> AvailableLibraries = new(16);

        public static void LoadNativeLibraries()
        {
            foreach (var p in NativeLibraryPaths)
            {
                var loadResult = NativeMethods.LoadLibrary(p);
                if (!loadResult.IsNull())
                {
                    AvailableLibraries.Add(p, loadResult);
                }
            }
        }

        private static class NativeMethods
        {
            [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr LoadLibrary(string dllToLoad);
        }

        public static bool HasFlag(this int flagsVariable, int flagValue) => (flagsVariable & flagValue) != 0;

        public static int AV_CEIL_RSHIFT(int a, int b) => ((a) + (1 << (b)) - 1) >> (b);


        /// <summary>
        /// Parses a hexagesimal or simple second and decimal string representing time
        /// and returns total microseconds.
        /// </summary>
        /// <param name="timeStr"></param>
        /// <returns></returns>
        public static long ParseTime(this string timeStr)
        {
            // HOURS:MM:SS.MILLISECONDS
            timeStr = timeStr.Trim();
            var segments = timeStr.Split(':', StringSplitOptions.TrimEntries).Reverse().ToArray();
            var span = (Hours: 0d, Minutes: 0d, Seconds: 0d);
            for (var i = 0; i < segments.Length; i++)
            {
                var value = double.TryParse(segments[i], out var parsedValue) ? parsedValue : 0;
                switch (i)
                {
                    case 0:
                        span.Seconds = value;
                        break;
                    case 1:
                        span.Minutes = value;
                        break;
                    case 2:
                        span.Hours = value;
                        break;
                    default:
                        break;
                }
            }

            var isNegative = span.Seconds < 0 || span.Hours < 0;
            var secondsSum = Math.Abs(span.Hours * 60 * 60) + Math.Abs(span.Minutes * 60) + Math.Abs(span.Seconds);
            var totalSeconds = secondsSum * (isNegative ? -1d : 1d);
            return Convert.ToInt64(totalSeconds) * ffmpeg.AV_TIME_BASE;
        }


        public static int Clamp(this int number, int min, int max) => number < min ? min : number > max ? max : number;

        public static double ToFactor(this AVRational r) => ffmpeg.av_q2d(r);

        public static double ToDouble(this int m) => Convert.ToDouble(m);

        public static double hypot(double s1, double s2) => Math.Sqrt((s1 * s1) + (s2 * s2));

        /// <summary>
        /// Port of av_display_rotation_get.
        /// </summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        public static unsafe double ComputeMatrixRotation(int* matrix)
        {
            var scale = new double[2];
            scale[0] = hypot(matrix[0].ToDouble(), matrix[3].ToDouble());
            scale[1] = hypot(matrix[1].ToDouble(), matrix[4].ToDouble());

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
                Helpers.LogWarning("Odd rotation angle.\n" +
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

        public static unsafe int check_stream_specifier(AVFormatContext* s, AVStream* st, string spec)
        {
            int ret = ffmpeg.avformat_match_stream_specifier(s, st, spec);
            if (ret < 0)
                Log(s, ffmpeg.AV_LOG_ERROR, $"Invalid stream specifier: {spec}.\n");

            return ret;
        }

        public static unsafe string PtrToString(byte* ptr) => PtrToString((IntPtr)ptr);

        public static unsafe string PtrToString(IntPtr ptr) => Marshal.PtrToStringUTF8(ptr);

        public static unsafe AVDictionary* filter_codec_opts(AVDictionary* opts, AVCodecID codec_id,
                                    AVFormatContext* s, AVStream* st, AVCodec* codec)
        {
            AVDictionary* ret = null;
            AVDictionaryEntry* t = null;
            int flags = s->oformat != null ? ffmpeg.AV_OPT_FLAG_ENCODING_PARAM : ffmpeg.AV_OPT_FLAG_DECODING_PARAM;
            byte prefix = 0;
            var cc = ffmpeg.avcodec_get_class();

            if (codec == null)
                codec = s->oformat != null ? ffmpeg.avcodec_find_encoder(codec_id) : ffmpeg.avcodec_find_decoder(codec_id);

            switch (st->codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    prefix = Convert.ToByte('v');
                    flags |= ffmpeg.AV_OPT_FLAG_VIDEO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    prefix = Convert.ToByte('a');
                    flags |= ffmpeg.AV_OPT_FLAG_AUDIO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    prefix = Convert.ToByte('s');
                    flags |= ffmpeg.AV_OPT_FLAG_SUBTITLE_PARAM;
                    break;
            }

            while ((t = ffmpeg.av_dict_get(opts, "", t, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var p = strchr(t->key, ':');

                /* check stream specification in opt name */
                if (p != null)
                    switch (check_stream_specifier(s, st, PtrToString(p + 1)))
                    {
                        case 1: *p = 0; break;
                        default: continue;
                            // default: exit_program(1);
                    }

                if (ffmpeg.av_opt_find(&cc, PtrToString(t->key), null, flags, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ) != null ||
                    codec == null ||
                    (codec->priv_class != null &&
                     ffmpeg.av_opt_find(&codec->priv_class, PtrToString(t->key), null, flags, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ) != null))
                    ffmpeg.av_dict_set(&ret, PtrToString(t->key), PtrToString(t->value), 0);
                else if (t->key[0] == prefix &&
                         ffmpeg.av_opt_find(&cc, PtrToString(t->key + 1), null, flags, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ) != null)
                    ffmpeg.av_dict_set(&ret, PtrToString(t->key + 1), PtrToString(t->value), 0);

                if (p != null)
                    *p = Convert.ToByte(':');
            }
            return ret;
        }

        public static unsafe void Log(void* opaque, int logLevel, string message)
            => ffmpeg.av_log(opaque, logLevel, message);

        public static unsafe void LogError(string message) => Log(null, ffmpeg.AV_LOG_ERROR, message);

        public static unsafe void LogError(AVCodecContext* context, string message) => Log(context, ffmpeg.AV_LOG_ERROR, message);

        public static unsafe void LogWarning(string message) => Log(null, ffmpeg.AV_LOG_WARNING, message);

        public static unsafe void LogWarning(AVCodecContext* context, string message) => Log(context, ffmpeg.AV_LOG_WARNING, message);

        public static unsafe void LogFatal(string message) => Log(null, ffmpeg.AV_LOG_FATAL, message);

        public static unsafe void LogDebug(string message) => Log(null, ffmpeg.AV_LOG_DEBUG, message);

        public static unsafe void LogInfo(string message) => Log(null, ffmpeg.AV_LOG_INFO, message);

        public static unsafe void LogVerbose(string message) => Log(null, ffmpeg.AV_LOG_VERBOSE, message);

        public static unsafe void LogTrace(string message) => Log(null, ffmpeg.AV_LOG_TRACE, message);

        public static unsafe void LogQuiet(string message) => Log(null, ffmpeg.AV_LOG_QUIET, message);

        public static unsafe string print_error(int errorCode)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(errorCode, buffer, (ulong)bufferSize);
            var message = PtrToString(buffer);
            return message;
        }

        public static unsafe AVDictionary** setup_find_stream_info_opts(AVFormatContext* s, AVDictionary* codec_opts)
        {
            if (s->nb_streams == 0)
                return null;

            var opts = (AVDictionary**)ffmpeg.av_mallocz_array(s->nb_streams, (ulong)sizeof(IntPtr));
            if (opts == null)
            {
                LogError("Could not alloc memory for stream options.\n");
                return null;
            }

            for (var i = 0; i < s->nb_streams; i++)
                opts[i] = filter_codec_opts(codec_opts, s->streams[i]->codecpar->codec_id, s, s->streams[i], null);

            return opts;
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
