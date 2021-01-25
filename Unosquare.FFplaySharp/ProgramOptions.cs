namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class ProgramOptions
    {
        /* options specified by the user */
        public AVInputFormat* file_iformat;
        public string input_filename;
        public int screen_width;
        public int screen_height;
        public bool is_full_screen;
        public bool audio_disable;
        public bool video_disable;
        public bool subtitle_disable;
        public Dictionary<AVMediaType, string> wanted_stream_spec;
        public int seek_by_bytes = -1;
        public float seek_interval = 1;
        public bool display_disable;
        public bool borderless;
        public bool alwaysontop;
        public int startup_volume = 100;
        public int show_status = -1;
        public ClockSync av_sync_type = ClockSync.Audio;
        public long start_time = ffmpeg.AV_NOPTS_VALUE;
        public long duration = ffmpeg.AV_NOPTS_VALUE;
        public int fast = 0;
        public bool genpts = false;
        public int lowres = 0;
        public int decoder_reorder_pts = -1;
        public bool autoexit;
        public bool exit_on_keydown;
        public bool exit_on_mousedown;
        public int loop = 1;
        public int framedrop = -1;
        public int infinite_buffer = -1;
        public ShowMode show_mode = ShowMode.None;
        public string AudioForcedCodecName;
        public string SubtitleForcedCodecName;
        public string VideoForcedCodecName;

        public double cursor_last_shown;
        public bool cursor_hidden = false;

        public List<string> vfilters_list = new(32);
        public int nb_vfilters = 0;
        public string afilters;

        public bool autorotate = true;
        public bool find_stream_info = true;
        public int filter_nbthreads = 0;

        /* From cmdutils.c */
        public AVDictionary* sws_dict;
        public AVDictionary* swr_opts;
        public AVDictionary* format_opts;
        public AVDictionary* codec_opts;

        public ProgramOptions()
        {
            wanted_stream_spec = new Dictionary<AVMediaType, string>();
            wanted_stream_spec[AVMediaType.AVMEDIA_TYPE_AUDIO] = null;
            wanted_stream_spec[AVMediaType.AVMEDIA_TYPE_VIDEO] = null;
            wanted_stream_spec[AVMediaType.AVMEDIA_TYPE_SUBTITLE] = null;
        }

        public void uninit_opts()
        {
            var r_swr_opts = swr_opts;
            var r_sws_dict = sws_dict;
            var r_format_opts = format_opts;
            var r_codec_opts = codec_opts;

            ffmpeg.av_dict_free(&r_swr_opts);
            ffmpeg.av_dict_free(&r_sws_dict);
            ffmpeg.av_dict_free(&r_format_opts);
            ffmpeg.av_dict_free(&r_codec_opts);

            swr_opts = null;
            sws_dict = null;
            format_opts = null;
            codec_opts = null;
        }

        public int opt_add_vfilter(void* optctx, string opt, string arg)
        {
            vfilters_list.Add(arg);
            return 0;
        }

        public static ProgramOptions FromCommandLineArguments(string[] args)
        {
            var options = new ProgramOptions();
            var arguments = args.ToList();

            for (var i = 0; i < arguments.Count; i++)
            {
                var argumentName = arguments[i];
                if (i == 0)
                {
                    if (argumentName == "-")
                    {
                        options.input_filename = "-";
                        continue;
                    }
                    else if (!argumentName.StartsWith("-"))
                    {
                        options.input_filename = argumentName;
                        continue;
                    }
                }

                if (argumentName.StartsWith("-"))
                    argumentName = argumentName.TrimStart('-').ToLowerInvariant();
                else
                    continue;

                var definition = Definitions.Where(d => d.Name == argumentName || d.ArgumentName == argumentName).FirstOrDefault();

                if (definition == null)
                    continue;

                var argumentValue = string.Empty;
                if (definition.Flags.HasFlag(OptionFlags.HAS_ARG))
                {
                    i++;
                    argumentValue = arguments[i];
                }

                definition.Apply(options, argumentValue);
            }

            if (options.input_filename == "-")
                options.input_filename = "pipe:";

            return options;
        }

        public static IReadOnlyList<OptionDef<ProgramOptions>> Definitions = new List<OptionDef<ProgramOptions>>
        {
            Option("x", true, "force displayed width", "width", (t, a) => t.screen_width = int.TryParse(a, out var v) ? v : t.screen_width),
            Option("y", true, "force displayed height", "height", (t, a) => t.screen_height = int.TryParse(a, out var v) ? v : t.screen_height),
            Option("s", true, "set frame size (WxH or abbreviation)", "size", (t, a) =>
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, "Option -s is deprecated, use -video_size.\n");
                t.opt_default(null, "video_size", a);
            }),
            Option("fs", false, "force full screen", (t, a) => t.is_full_screen = true),
            Option("an", false, "disable audio", (t, a) => t.audio_disable = true),
            Option("vn", false, "disable video", (t, a) => t.video_disable = true),
            Option("sn", false, "disable subtitling", (t, a) => t.subtitle_disable = true),
            Option("ast", true, "select desired audio stream", "stream_specifier", (t, a) =>
                t.wanted_stream_spec[AVMediaType.AVMEDIA_TYPE_AUDIO] = a),
            Option("vst", true, "select desired video stream", "stream_specifier", (t, a) =>
                t.wanted_stream_spec[AVMediaType.AVMEDIA_TYPE_VIDEO] = a),
            Option("sst", true, "select desired subtitle stream", "stream_specifier", (t, a) =>
                t.wanted_stream_spec[AVMediaType.AVMEDIA_TYPE_SUBTITLE] = a),

            Option("ss", true, "seek to a given position in seconds", "pos", (t, a) => t.start_time = ParseTime(a)),
            Option("t", true, "play  \"duration\" seconds of audio/video", "duration", (t, a) => t.duration = ParseTime(a)),
            Option("bytes", true, "seek by bytes 0=off 1=on -1=auto", "val", (t, a) => t.seek_by_bytes = int.TryParse(a, out var v) ? v : t.seek_by_bytes),


            Option("f", true, "force format", "fmt", (t, a) => t.file_iformat = ffmpeg.av_find_input_format(a)),
            Option("i", true, "read specified file", "input_file", (t, a) => t.input_filename = a)
        };

        public static long ParseTime(string timeStr)
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

        private static bool TryParseBool(string arg, out bool value)
        {
            value = false;

            if (string.IsNullOrWhiteSpace(arg))
                return false;

            value = arg == "0" || arg == "false" || arg == "no";
            return true;
        }

        private static OptionDef<ProgramOptions> Option(string name, OptionFlags flags, string help, string argName, Action<ProgramOptions, string> apply)
            => new OptionDef<ProgramOptions>(name, flags, apply, help, argName);

        private static OptionDef<ProgramOptions> Option(string name, bool hasArgument, string help, string argName, Action<ProgramOptions, string> apply)
            => new OptionDef<ProgramOptions>(name, hasArgument ? OptionFlags.HAS_ARG : OptionFlags.OPT_BOOL, apply, help, argName);

        private static OptionDef<ProgramOptions> Option(string name, OptionFlags flags, string help, Action<ProgramOptions, string> apply)
            => new OptionDef<ProgramOptions>(name, flags, apply, help);

        private static OptionDef<ProgramOptions> Option(string name, bool hasArgument, string help, Action<ProgramOptions, string> apply)
            => new OptionDef<ProgramOptions>(name, hasArgument ? OptionFlags.HAS_ARG : OptionFlags.OPT_BOOL, apply, help);

        private static AVOption* opt_find(void* obj, string name, string unit, int opt_flags, int search_flags)
        {
            var o = ffmpeg.av_opt_find(obj, name, unit, opt_flags, search_flags);
            if (o != null && o->flags == 0)
                return null;

            return o;
        }

        private static int FLAGS(AVOption* o, string arg)
        {
            return (o->type == AVOptionType.AV_OPT_TYPE_FLAGS && (arg[0] == '-' || arg[0] == '+')) ? ffmpeg.AV_DICT_APPEND : 0;
        }

        private unsafe int opt_default(void* optctx, string opt, string arg)
        {
            const int SearchFlags = ffmpeg.AV_OPT_SEARCH_CHILDREN | ffmpeg.AV_OPT_SEARCH_FAKE_OBJ;
            AVOption* o;
            bool consumed = false;
            var cc = ffmpeg.avcodec_get_class();
            var fc = ffmpeg.avformat_get_class();
            var sc = ffmpeg.sws_get_class();
            var swr_class = ffmpeg.swr_get_class();

            var codecRef = codec_opts;
            var formatRef = format_opts;
            var swsRef = sws_dict;
            var swrRef = swr_opts;

            if (opt == "debug" || opt == "fdebug")
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_DEBUG);

            // Example: -codec:a:1 ac3
            var opt_stripped = opt.Contains(':') ? opt.Substring(0, opt.IndexOf(':')) : new string(opt);

            if ((o = opt_find(&cc, opt_stripped, null, 0, SearchFlags)) != null ||
                ((opt[0] == 'v' || opt[0] == 'a' || opt[0] == 's') &&
                 (o = opt_find(&cc, opt + 1, null, 0, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ)) != null))
            {
                ffmpeg.av_dict_set(&codecRef, opt, arg, FLAGS(o, arg));
                codec_opts = codecRef;
                consumed = true;
            }
            if ((o = opt_find(&fc, opt, null, 0, SearchFlags)) != null)
            {
                ffmpeg.av_dict_set(&formatRef, opt, arg, FLAGS(o, arg));
                format_opts = formatRef;
                if (consumed)
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Routing option {opt} to both codec and muxer layer\n");
                consumed = true;
            }

            if (!consumed && (o = opt_find(&sc, opt, null, 0, SearchFlags)) != null)
            {
                var sws = ffmpeg.sws_alloc_context();
                int ret = ffmpeg.av_opt_set(sws, opt, arg, 0);
                ffmpeg.sws_freeContext(sws);
                if (opt == "srcw" || opt == "srch" ||
                    opt == "dstw" || opt == "dsth" ||
                    opt == "src_format" || opt == "dst_format")
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Directly using swscale dimensions/format options is not supported, please use the -s or -pix_fmt options\n");
                    return ffmpeg.AVERROR(ffmpeg.EINVAL);
                }
                if (ret < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Error setting option {opt}.\n");
                    return ret;
                }

                ffmpeg.av_dict_set(&swsRef, opt, arg, FLAGS(o, arg));
                sws_dict = swsRef;

                consumed = true;
            }

            if (!consumed && (o = opt_find(&swr_class, opt, null, 0, SearchFlags)) != null)
            {
                var swr = ffmpeg.swr_alloc();
                int ret = ffmpeg.av_opt_set(swr, opt, arg, 0);
                ffmpeg.swr_free(&swr);
                if (ret < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Error setting option {opt}.\n");
                    return ret;
                }
                ffmpeg.av_dict_set(&swrRef, opt, arg, FLAGS(o, arg));
                swr_opts = swrRef;
                consumed = true;
            }

            if (consumed)
                return 0;

            return ffmpeg.AVERROR_OPTION_NOT_FOUND;
        }
    }
}
