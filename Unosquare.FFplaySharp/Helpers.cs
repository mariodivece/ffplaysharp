namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;

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
                if (loadResult != IntPtr.Zero)
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


        public static int AV_CEIL_RSHIFT(int a, int b) => ((a) + (1 << (b)) - 1) >> (b);

        public static int av_clip(int number, int min, int max) => number < min ? min : number > max ? max : number;

        public static bool cmp_audio_fmts(AVSampleFormat fmt1, long channel_count1, AVSampleFormat fmt2, long channel_count2)
        {
            /* If channel count == 1, planar and non-planar formats are the same */
            if (channel_count1 == 1 && channel_count2 == 1)
                return ffmpeg.av_get_packed_sample_fmt(fmt1) != ffmpeg.av_get_packed_sample_fmt(fmt2);
            else
                return channel_count1 != channel_count2 || fmt1 != fmt2;
        }

        public static ulong get_valid_channel_layout(ulong channel_layout, int channels)
        {
            if (channel_layout != 0 && ffmpeg.av_get_channel_layout_nb_channels(channel_layout) == channels)
                return channel_layout;
            else
                return 0;
        }

        public static unsafe void FFSWAP(ref AVFilterContext** array, int a, int b)
        {
            var temp = array[b];
            array[b] = array[a];
            array[a] = temp;
        }

        public static unsafe bool INSERT_FILT(string name, string arg, ref AVFilterGraph* graph, ref int ret, ref AVFilterContext* last_filter)
        {
            do
            {
                AVFilterContext* filt_ctx;

                ret = ffmpeg.avfilter_graph_create_filter(&filt_ctx,
                                                   ffmpeg.avfilter_get_by_name(name),
                                                   $"ffplay_{name}", arg, null, graph);
                if (ret < 0)
                    return false;

                ret = ffmpeg.avfilter_link(filt_ctx, 0, last_filter, 0);
                if (ret < 0)
                    return false;

                last_filter = filt_ctx;
            } while (false);

            return true;
        }

        public static double CONV_FP(int m) => Convert.ToDouble(m);

        public static double hypot(double s1, double s2) => Math.Sqrt((s1 * s1) + (s2 * s2));

        public static unsafe double av_display_rotation_get(int* matrix)
        {
            double rotation;
            var scale = new double[2];
            var num = matrix[0];

            scale[0] = hypot(CONV_FP(matrix[0]), CONV_FP(matrix[3]));
            scale[1] = hypot(CONV_FP(matrix[1]), CONV_FP(matrix[4]));

            if (scale[0] == 0.0 || scale[1] == 0.0)
                return double.NaN;

            rotation = Math.Atan2(CONV_FP(matrix[1]) / scale[1],
                             CONV_FP(matrix[0]) / scale[0]) * 180 / Math.PI;

            return -rotation;
        }

        public static unsafe double get_rotation(AVStream* st)
        {
            var displaymatrix = ffmpeg.av_stream_get_side_data(st, AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX, null);
            double theta = 0;
            if (displaymatrix != null)
                theta = -av_display_rotation_get((int*)displaymatrix);

            theta -= 360 * Math.Floor(theta / 360 + 0.9 / 360);

            if (Math.Abs(theta - 90 * Math.Round(theta / 90, 0)) > 2)
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, "Odd rotation angle.\n" +
                       "If you want to help, upload a sample " +
                       "of this file to https://streams.videolan.org/upload/ " +
                       "and contact the ffmpeg-devel mailing list. (ffmpeg-devel@ffmpeg.org)");

            return theta;
        }

        public static unsafe int av_opt_set_int_list(void* obj, string name, int[] val, int flags)
        {
            // TODO: audio not working with this function enabled. ???
            // return 0;

            fixed (int* ptr = &val[0])
                return ffmpeg.av_opt_set_bin(obj, name, (byte*)ptr, val.Length * sizeof(int), flags);
        }

        public static unsafe int av_opt_set_int_list(void* obj, string name, long[] val, int flags)
        {
            // TODO: audio not working with this function enabled. ???
            // return 0;

            fixed (long* ptr = &val[0])
                return ffmpeg.av_opt_set_bin(obj, name, (byte*)ptr, val.Length * sizeof(long), flags);
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
                ffmpeg.av_log(s, ffmpeg.AV_LOG_ERROR, $"Invalid stream specifier: {spec}.\n");
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
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Could not alloc memory for stream options.\n");
                return null;
            }

            for (var i = 0; i < s->nb_streams; i++)
                opts[i] = filter_codec_opts(codec_opts, s->streams[i]->codecpar->codec_id, s, s->streams[i], null);

            return opts;
        }
    }
}
