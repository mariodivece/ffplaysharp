namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public unsafe class ProgramOptions
    {
        /* options specified by the user */
        public AVInputFormat* file_iformat;
        public string input_filename;

        public bool audio_disable;
        public bool video_disable;
        public bool subtitle_disable;
        public string[] wanted_stream_spec = new string[(int)AVMediaType.AVMEDIA_TYPE_NB];
        public int seek_by_bytes = -1;
        public float seek_interval = 10;
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

        public long cursor_last_shown;
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
        public AVDictionary* resample_opts;

        // inlined static variables
        public long last_time_status = 0;
        public double last_audio_clock = 0;

        public void uninit_opts()
        {
            var r_swr_opts = swr_opts;
            var r_sws_dict = sws_dict;
            var r_format_opts = format_opts;
            var r_codec_opts = codec_opts;
            var r_resample_opts = resample_opts;

            ffmpeg.av_dict_free(&r_swr_opts);
            ffmpeg.av_dict_free(&r_sws_dict);
            ffmpeg.av_dict_free(&r_format_opts);
            ffmpeg.av_dict_free(&r_codec_opts);
            ffmpeg.av_dict_free(&r_resample_opts);

            swr_opts = null;
            sws_dict = null;
            format_opts = null;
            codec_opts = null;
            resample_opts = null;
        }

        public int opt_add_vfilter(void* optctx, string opt, string arg)
        {
            vfilters_list.Add(arg);
            return 0;
        }
    }
}
