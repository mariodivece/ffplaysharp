namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;

    public unsafe class MediaContainer
    {
        public Thread read_tid;
        public AVInputFormat* iformat;
        public bool abort_request;
        public bool force_refresh;
        public bool paused;
        public bool last_paused;
        public bool queue_attachments_req;
        public bool seek_req;
        public int seek_flags;
        public long seek_pos;
        public long seek_rel;
        public int read_pause_return;
        public AVFormatContext* ic;
        public bool realtime;

        public Clock AudioClock;
        public Clock VideoClock;
        public Clock ExternalClock;

        public AudioComponent Audio { get; }

        public VideoComponent Video { get; }

        public SubtitleComponent Subtitle { get; }

        public ClockSync ClockSyncMode;

        public double audio_clock;
        public int audio_clock_serial;
        public double audio_diff_cum; /* used for AV difference average computation */
        public double audio_diff_avg_coef;
        public double audio_diff_threshold;
        public int audio_diff_avg_count;


        public int audio_hw_buf_size;
        public byte* audio_buf;
        public byte* audio_buf1;
        public uint audio_buf_size; /* in bytes */
        public uint audio_buf1_size;
        public int audio_buf_index; /* in bytes */
        public int audio_write_buf_size;
        public int audio_volume;
        public bool muted;


        public int frame_drops_early;
        public int frame_drops_late;

        public ShowMode show_mode;

        public short[] sample_array = new short[Constants.SAMPLE_ARRAY_SIZE];
        public int sample_array_index;
        public int last_i_start;
        // RDFTContext* rdft;
        // int rdft_bits;
        // FFTSample* rdft_data;
        public int xpos;
        public double last_vis_time;
        public IntPtr vis_texture;
        public IntPtr sub_texture;
        public IntPtr vid_texture;

        public double frame_timer;
        public double frame_last_returned_time;
        public double frame_last_filter_delay;

        public double max_frame_duration;      // maximum duration of a frame - above this, we consider the jump a timestamp discontinuity

        public bool eof;

        public string filename;
        public int width = 1;
        public int height = 1;
        public int xleft;
        public int ytop;
        public int step;

        public int vfilter_idx;

        public AVFilterGraph* agraph;              // audio filter graph

        public AutoResetEvent continue_read_thread = new(false);

        public MediaContainer(ProgramOptions options)
        {
            Options = options ?? new();
            Audio = new(this);
            Video = new(this);
            Subtitle = new(this);
        }

        public ProgramOptions Options { get; }

        public ClockSync MasterSyncMode
        {
            get
            {
                if (ClockSyncMode == ClockSync.Video)
                {
                    if (Video.Stream != null)
                        return ClockSync.Video;
                    else
                        return ClockSync.Audio;
                }
                else if (ClockSyncMode == ClockSync.Audio)
                {
                    if (Audio.Stream != null)
                        return ClockSync.Audio;
                    else
                        return ClockSync.External;
                }
                else
                {
                    return ClockSync.External;
                }
            }
        }

        /* get the current master clock value */
        public double MasterTime
        {
            get
            {
                switch (MasterSyncMode)
                {
                    case ClockSync.Video:
                        return VideoClock.Time;
                    case ClockSync.Audio:
                        return AudioClock.Time;
                    default:
                        return ExternalClock.Time;
                }
            }
        }


        public int configure_audio_filters(bool force_output_format)
        {
            var afilters = Options.afilters;
            var sample_fmts = new[] { (int)AVSampleFormat.AV_SAMPLE_FMT_S16 };
            var sample_rates = new[] { 0 };
            var channel_layouts = new[] { 0L };
            var channels = new[] { 0 };

            AVFilterContext* filt_asrc = null, filt_asink = null;
            string aresample_swr_opts = string.Empty;
            AVDictionaryEntry* e = null;
            string asrc_args = null;
            int ret;

            fixed (AVFilterGraph** agraphptr = &agraph)
                ffmpeg.avfilter_graph_free(agraphptr);

            if ((agraph = ffmpeg.avfilter_graph_alloc()) == null)
                return ffmpeg.AVERROR(ffmpeg.ENOMEM);
            agraph->nb_threads = Options.filter_nbthreads;

            while ((e = ffmpeg.av_dict_get(Options.swr_opts, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString((IntPtr)e->key);
                var value = Helpers.PtrToString((IntPtr)e->value);
                aresample_swr_opts = $"{key}={value}:{aresample_swr_opts}";
            }

            if (string.IsNullOrWhiteSpace(aresample_swr_opts))
                aresample_swr_opts = null;

            ffmpeg.av_opt_set(agraph, "aresample_swr_opts", aresample_swr_opts, 0);
            asrc_args = $"sample_rate={Audio.FilterSpec.Frequency}:sample_fmt={ffmpeg.av_get_sample_fmt_name(Audio.FilterSpec.SampleFormat)}:" +
                $"channels={Audio.FilterSpec.Channels}:time_base={1}/{Audio.FilterSpec.Frequency}";

            if (Audio.FilterSpec.Layout != 0)
                asrc_args = $"{asrc_args}:channel_layout=0x{Audio.FilterSpec.Layout:x16}";

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asrc,
                                               ffmpeg.avfilter_get_by_name("abuffer"), "ffplay_abuffer",
                                               asrc_args, null, agraph);
            if (ret < 0)
                goto end;

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asink,
                                               ffmpeg.avfilter_get_by_name("abuffersink"), "ffplay_abuffersink",
                                               null, null, agraph);
            if (ret < 0)
                goto end;

            if ((ret = Helpers.av_opt_set_int_list(filt_asink, "sample_fmts", sample_fmts, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if ((ret = ffmpeg.av_opt_set_int(filt_asink, "all_channel_counts", 1, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if (force_output_format)
            {
                channel_layouts[0] = Convert.ToInt32(Audio.TargetSpec.Layout);
                channels[0] = Audio.TargetSpec.Layout != 0 ? -1 : Audio.TargetSpec.Channels;
                sample_rates[0] = Audio.TargetSpec.Frequency;
                if ((ret = ffmpeg.av_opt_set_int(filt_asink, "all_channel_counts", 0, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "channel_layouts", channel_layouts, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "channel_counts", channels, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "sample_rates", sample_rates, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
            }

            if ((ret = configure_filtergraph(agraph, afilters, filt_asrc, filt_asink)) < 0)
                goto end;

            Audio.InputFilter = filt_asrc;
            Audio.OutputFilter = filt_asink;

        end:
            if (ret < 0)
            {
                fixed (AVFilterGraph** agraphptr = &agraph)
                    ffmpeg.avfilter_graph_free(agraphptr);
            }
            return ret;
        }

        public static int configure_filtergraph(AVFilterGraph* graph, string filtergraph,
                                 AVFilterContext* source_ctx, AVFilterContext* sink_ctx)
        {
            int ret;
            var nb_filters = graph->nb_filters;
            AVFilterInOut* outputs = null, inputs = null;

            if (!string.IsNullOrWhiteSpace(filtergraph))
            {
                outputs = ffmpeg.avfilter_inout_alloc();
                inputs = ffmpeg.avfilter_inout_alloc();
                if (outputs == null || inputs == null)
                {
                    ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                    goto fail;
                }

                outputs->name = ffmpeg.av_strdup("in");
                outputs->filter_ctx = source_ctx;
                outputs->pad_idx = 0;
                outputs->next = null;

                inputs->name = ffmpeg.av_strdup("out");
                inputs->filter_ctx = sink_ctx;
                inputs->pad_idx = 0;
                inputs->next = null;

                if ((ret = ffmpeg.avfilter_graph_parse_ptr(graph, filtergraph, &inputs, &outputs, null)) < 0)
                    goto fail;
            }
            else
            {
                if ((ret = ffmpeg.avfilter_link(source_ctx, 0, sink_ctx, 0)) < 0)
                    goto fail;
            }

            /* Reorder the filters to ensure that inputs of the custom filters are merged first */
            for (var i = 0; i < graph->nb_filters - nb_filters; i++)
                Helpers.FFSWAP(ref graph->filters, i, i + (int)nb_filters);

            ret = ffmpeg.avfilter_graph_config(graph, null);
        fail:
            ffmpeg.avfilter_inout_free(&outputs);
            ffmpeg.avfilter_inout_free(&inputs);
            return ret;
        }

        public void check_external_clock_speed()
        {
            if (Video.StreamIndex >= 0 && Video.Packets.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES ||
                Audio.StreamIndex >= 0 && Audio.Packets.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES)
            {
                ExternalClock.SetSpeed(Math.Max(Constants.EXTERNAL_CLOCK_SPEED_MIN, ExternalClock.SpeedRatio - Constants.EXTERNAL_CLOCK_SPEED_STEP));
            }
            else if ((Video.StreamIndex < 0 || Video.Packets.Count > Constants.EXTERNAL_CLOCK_MAX_FRAMES) &&
                     (Audio.StreamIndex < 0 || Audio.Packets.Count > Constants.EXTERNAL_CLOCK_MAX_FRAMES))
            {
                ExternalClock.SetSpeed(Math.Min(Constants.EXTERNAL_CLOCK_SPEED_MAX, ExternalClock.SpeedRatio + Constants.EXTERNAL_CLOCK_SPEED_STEP));
            }
            else
            {
                var speed = ExternalClock.SpeedRatio;
                if (speed != 1.0)
                    ExternalClock.SetSpeed(speed + Constants.EXTERNAL_CLOCK_SPEED_STEP * (1.0 - speed) / Math.Abs(1.0 - speed));
            }
        }

        public void stream_toggle_pause()
        {
            if (paused)
            {
                frame_timer += ffmpeg.av_gettime_relative() / 1000000.0 - VideoClock.LastUpdated;
                if (read_pause_return != ffmpeg.AVERROR(38))
                {
                    VideoClock.IsPaused = false;
                }
                VideoClock.Set(VideoClock.Time, VideoClock.Serial);
            }

            ExternalClock.Set(ExternalClock.Time, ExternalClock.Serial);
            paused = AudioClock.IsPaused = VideoClock.IsPaused = ExternalClock.IsPaused = !paused;
        }
    }
}
