namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Threading;

    public unsafe class MediaContainer
    {
        public Thread read_tid;
        public AVInputFormat* iformat = null;
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
        public AVFormatContext* InputContext = null;
        public bool realtime;

        public Clock AudioClock;
        public Clock VideoClock;
        public Clock ExternalClock;

        public AudioComponent Audio { get; }

        public VideoComponent Video { get; }

        public SubtitleComponent Subtitle { get; }

        public ClockSync ClockSyncMode { get; set; }

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

        public short* sample_array;
        public int sample_array_index;
        public int last_i_start;
        // RDFTContext* rdft;
        // int rdft_bits;
        // FFTSample* rdft_data;
        public int xpos;
        public double last_vis_time;
        public IntPtr vis_texture; // TODO: remove (audio visualization texture)
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
        public MediaRenderer Renderer { get; }

        public AVFilterGraph* agraph = null;              // audio filter graph

        public AutoResetEvent continue_read_thread = new(false);

        public MediaContainer(ProgramOptions options, MediaRenderer renderer)
        {
            Options = options ?? new();
            Audio = new(this);
            Video = new(this);
            Subtitle = new(this);
            Renderer = renderer;
            Components = new List<MediaComponent>() { Audio, Video, Subtitle };

            sample_array = (short*)ffmpeg.av_mallocz(Constants.SAMPLE_ARRAY_SIZE * sizeof(short));
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

        public IReadOnlyList<MediaComponent> Components { get; }

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

            var audioFilterGraph = agraph;
            // TODO: sometimes agraph has weird memory.
            if (audioFilterGraph != null && audioFilterGraph->nb_filters > 0)
                ffmpeg.avfilter_graph_free(&audioFilterGraph);
            agraph = ffmpeg.avfilter_graph_alloc();
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
                var audioGraphRef = agraph;
                ffmpeg.avfilter_graph_free(&audioGraphRef);
                agraph = null;
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

        public void stream_cycle_channel(AVMediaType codecType)
        {
            AVFormatContext* ic = InputContext;
            var streamCount = (int)ic->nb_streams;

            MediaComponent component = codecType switch
            {
                AVMediaType.AVMEDIA_TYPE_VIDEO => Video,
                AVMediaType.AVMEDIA_TYPE_AUDIO => Audio,
                _ => Subtitle
            };

            var startStreamIndex = component.LastStreamIndex;
            var nextStreamIndex = startStreamIndex;

            AVProgram* program = null;
            if (component.IsVideo && component.StreamIndex != -1)
            {
                program = ffmpeg.av_find_program_from_stream(ic, null, component.StreamIndex);
                if (program != null)
                {
                    streamCount = (int)program->nb_stream_indexes;
                    for (startStreamIndex = 0; startStreamIndex < streamCount; startStreamIndex++)
                        if (program->stream_index[startStreamIndex] == nextStreamIndex)
                            break;
                    if (startStreamIndex == streamCount)
                        startStreamIndex = -1;
                    nextStreamIndex = startStreamIndex;
                }
            }

            while (true)
            {
                if (++nextStreamIndex >= streamCount)
                {
                    if (component.MediaType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        nextStreamIndex = -1;
                        Subtitle.LastStreamIndex = -1;
                        goto the_end;
                    }
                    if (startStreamIndex == -1)
                        return;

                    nextStreamIndex = 0;
                }

                if (nextStreamIndex == startStreamIndex)
                    return;

                var st = InputContext->streams[program != null ? program->stream_index[nextStreamIndex] : nextStreamIndex];
                if (st->codecpar->codec_type == component.MediaType)
                {
                    /* check that parameters are OK */
                    switch (component.MediaType)
                    {
                        case AVMediaType.AVMEDIA_TYPE_AUDIO:
                            if (st->codecpar->sample_rate != 0 &&
                                st->codecpar->channels != 0)
                                goto the_end;
                            break;
                        case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                            goto the_end;
                        default:
                            break;
                    }
                }
            }
        the_end:
            if (program != null && nextStreamIndex != -1)
                nextStreamIndex = (int)program->stream_index[nextStreamIndex];
            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"Switch {ffmpeg.av_get_media_type_string(component.MediaType)} stream from #{component.StreamIndex} to #{nextStreamIndex}\n");

            component.Close();
            stream_component_open(nextStreamIndex);
        }

        public void StartReadThread()
        {
            read_tid = new Thread(read_thread) { IsBackground = true, Name = nameof(read_thread) };
            read_tid.Start();
        }

        private int decode_interrupt_cb(void* ctx)
        {
            return abort_request ? 1 : 0;
        }

        static bool is_realtime(AVFormatContext* ic)
        {
            var iformat = Helpers.PtrToString(ic->iformat->name);
            if (iformat == "rtp" || iformat == "rtsp" || iformat == "sdp")
                return true;

            var url = Helpers.PtrToString(ic->url);
            url = string.IsNullOrEmpty(url) ? string.Empty : url;

            if (ic->pb != null && (url.StartsWith("rtp:") || url.StartsWith("udp:")))
                return true;

            return false;
        }

        public void step_to_next_frame()
        {
            /* if the stream is paused unpause it, then step */
            if (paused)
                stream_toggle_pause();
            step = 1;
        }

        /* seek in the stream */
        public void stream_seek(long pos, long rel, int seek_by_bytes)
        {
            if (seek_req)
                return;

            seek_pos = pos;
            seek_rel = rel;
            seek_flags &= ~ffmpeg.AVSEEK_FLAG_BYTE;
            if (seek_by_bytes != 0)
                seek_flags |= ffmpeg.AVSEEK_FLAG_BYTE;
            seek_req = true;
            continue_read_thread.Set();
        }

        public void seek_chapter(int incr)
        {
            var i = 0;
            var pos = (long)(MasterTime * ffmpeg.AV_TIME_BASE);

            if (InputContext->nb_chapters <= 0)
                return;

            /* find the current chapter */
            for (i = 0; i < InputContext->nb_chapters; i++)
            {
                AVChapter* ch = InputContext->chapters[i];
                if (ffmpeg.av_compare_ts(pos, Constants.AV_TIME_BASE_Q, ch->start, ch->time_base) < 0)
                {
                    i--;
                    break;
                }
            }

            i += incr;
            i = Math.Max(i, 0);
            if (i >= InputContext->nb_chapters)
                return;

            ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Seeking to chapter {i}.\n");
            stream_seek(ffmpeg.av_rescale_q(InputContext->chapters[i]->start, InputContext->chapters[i]->time_base, Constants.AV_TIME_BASE_Q), 0, 0);
        }

        /* open a given stream. Return 0 if OK */
        public int stream_component_open(int stream_index)
        {
            AVFormatContext* ic = InputContext;
            AVCodecContext* avctx;
            AVCodec* codec;
            string forcedCodecName = null;
            AVDictionary* opts = null;
            AVDictionaryEntry* t = null;
            int sampleRate, nb_channels;
            long channelLayout;
            int ret = 0;
            int stream_lowres = Options.lowres;

            if (stream_index < 0 || stream_index >= ic->nb_streams)
                return -1;

            avctx = ffmpeg.avcodec_alloc_context3(null);
            if (avctx == null)
                return ffmpeg.AVERROR(ffmpeg.ENOMEM);

            ret = ffmpeg.avcodec_parameters_to_context(avctx, ic->streams[stream_index]->codecpar);
            if (ret < 0) goto fail;
            avctx->pkt_timebase = ic->streams[stream_index]->time_base;

            codec = ffmpeg.avcodec_find_decoder(avctx->codec_id);

            switch (avctx->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO: Audio.LastStreamIndex = stream_index; forcedCodecName = Options.AudioForcedCodecName; break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE: Subtitle.LastStreamIndex = stream_index; forcedCodecName = Options.SubtitleForcedCodecName; break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO: Video.LastStreamIndex = stream_index; forcedCodecName = Options.VideoForcedCodecName; break;
            }
            if (!string.IsNullOrWhiteSpace(forcedCodecName))
                codec = ffmpeg.avcodec_find_decoder_by_name(forcedCodecName);

            if (codec == null)
            {
                if (!string.IsNullOrWhiteSpace(forcedCodecName))
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"No codec could be found with name '{forcedCodecName}'\n");
                else
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"No decoder could be found for codec {ffmpeg.avcodec_get_name(avctx->codec_id)}\n");
                ret = ffmpeg.AVERROR(ffmpeg.EINVAL);
                goto fail;
            }

            avctx->codec_id = codec->id;
            if (stream_lowres > codec->max_lowres)
            {
                ffmpeg.av_log(avctx, ffmpeg.AV_LOG_WARNING, $"The maximum value for lowres supported by the decoder is {codec->max_lowres}\n");
                stream_lowres = codec->max_lowres;
            }

            avctx->lowres = stream_lowres;

            if (Options.fast != 0)
                avctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            opts = Helpers.filter_codec_opts(Options.codec_opts, avctx->codec_id, ic, ic->streams[stream_index], codec);
            if (ffmpeg.av_dict_get(opts, "threads", null, 0) == null)
                ffmpeg.av_dict_set(&opts, "threads", "auto", 0);

            if (stream_lowres != 0)
                ffmpeg.av_dict_set_int(&opts, "lowres", stream_lowres, 0);

            if (avctx->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO || avctx->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                ffmpeg.av_dict_set(&opts, "refcounted_frames", "1", 0);

            if ((ret = ffmpeg.avcodec_open2(avctx, codec, &opts)) < 0)
            {
                goto fail;
            }
            if ((t = ffmpeg.av_dict_get(opts, "", null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString(t->key);
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {key} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            eof = false;
            ic->streams[stream_index]->discard = AVDiscard.AVDISCARD_DEFAULT;
            switch (avctx->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    {
                        AVFilterContext* sink;
                        Audio.FilterSpec.Frequency = avctx->sample_rate;
                        Audio.FilterSpec.Channels = avctx->channels;
                        Audio.FilterSpec.Layout = (long)Helpers.get_valid_channel_layout(avctx->channel_layout, avctx->channels);
                        Audio.FilterSpec.SampleFormat = avctx->sample_fmt;
                        if ((ret = configure_audio_filters(false)) < 0)
                            goto fail;
                        sink = Audio.OutputFilter;
                        sampleRate = ffmpeg.av_buffersink_get_sample_rate(sink);
                        nb_channels = ffmpeg.av_buffersink_get_channels(sink);
                        channelLayout = (long)ffmpeg.av_buffersink_get_channel_layout(sink);
                    }

                    sampleRate = avctx->sample_rate;
                    nb_channels = avctx->channels;
                    channelLayout = (long)avctx->channel_layout;

                    /* prepare audio output */
                    if ((ret = audio_open(channelLayout, nb_channels, sampleRate, ref Audio.TargetSpec)) < 0)
                        goto fail;

                    audio_hw_buf_size = ret;
                    Audio.SourceSpec = Audio.TargetSpec;
                    audio_buf_size = 0;
                    audio_buf_index = 0;

                    /* init averaging filter */
                    audio_diff_avg_coef = Math.Exp(Math.Log(0.01) / Constants.AUDIO_DIFF_AVG_NB);
                    audio_diff_avg_count = 0;

                    /* since we do not have a precise anough audio FIFO fullness,
                       we correct audio sync only if larger than this threshold */
                    audio_diff_threshold = (double)(audio_hw_buf_size) / Audio.TargetSpec.BytesPerSecond;

                    Audio.StreamIndex = stream_index;
                    Audio.Stream = ic->streams[stream_index];

                    Audio.Decoder = new(this, avctx);
                    if ((InputContext->iformat->flags & (ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK)) != 0 &&
                        InputContext->iformat->read_seek.Pointer == IntPtr.Zero)
                    {
                        Audio.Decoder.StartPts = Audio.Stream->start_time;
                        Audio.Decoder.StartPtsTimeBase = Audio.Stream->time_base;
                    }

                    Audio.Decoder.Start();
                    Renderer.PauseAudio();
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    Video.StreamIndex = stream_index;
                    Video.Stream = ic->streams[stream_index];

                    Video.Decoder = new(this, avctx);
                    Video.Decoder.Start();
                    queue_attachments_req = true;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    Subtitle.StreamIndex = stream_index;
                    Subtitle.Stream = ic->streams[stream_index];

                    Subtitle.Decoder = new(this, avctx);
                    Subtitle.Decoder.Start();
                    break;
                default:
                    break;
            }
            goto @out;

        fail:
            ffmpeg.avcodec_free_context(&avctx);
        @out:
            ffmpeg.av_dict_free(&opts);

            return ret;
        }

        public int audio_open(long wanted_channel_layout, int wanted_nb_channels, int wanted_sample_rate, ref AudioParams audio_hw_params)
        {
            SDL.SDL_AudioSpec wanted_spec = new();
            SDL.SDL_AudioSpec spec = new();

            var next_nb_channels = new[] { 0, 0, 1, 6, 2, 6, 4, 6 };
            var next_sample_rates = new[] { 0, 44100, 48000, 96000, 192000 };
            int next_sample_rate_idx = next_sample_rates.Length - 1;

            var env = Environment.GetEnvironmentVariable("SDL_AUDIO_CHANNELS");
            if (!string.IsNullOrWhiteSpace(env))
            {
                wanted_nb_channels = int.Parse(env);
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_nb_channels);
            }

            if (wanted_channel_layout == 0 || wanted_nb_channels != ffmpeg.av_get_channel_layout_nb_channels((ulong)wanted_channel_layout))
            {
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_nb_channels);
                wanted_channel_layout &= ~ffmpeg.AV_CH_LAYOUT_STEREO_DOWNMIX;
            }

            wanted_nb_channels = ffmpeg.av_get_channel_layout_nb_channels((ulong)wanted_channel_layout);
            wanted_spec.channels = (byte)wanted_nb_channels;
            wanted_spec.freq = wanted_sample_rate;
            if (wanted_spec.freq <= 0 || wanted_spec.channels <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Invalid sample rate or channel count!\n");
                return -1;
            }

            while (next_sample_rate_idx != 0 && next_sample_rates[next_sample_rate_idx] >= wanted_spec.freq)
                next_sample_rate_idx--;

            wanted_spec.format = SDL.AUDIO_S16SYS;
            wanted_spec.silence = 0;
            wanted_spec.samples = (ushort)Math.Max(Constants.SDL_AUDIO_MIN_BUFFER_SIZE, 2 << ffmpeg.av_log2((uint)(wanted_spec.freq / Constants.SDL_AUDIO_MAX_CALLBACKS_PER_SEC)));
            wanted_spec.callback = Renderer.sdl_audio_callback;
            // wanted_spec.userdata = GCHandle.ToIntPtr(VideoStateHandle);
            while ((Renderer.audio_dev = SDL.SDL_OpenAudioDevice(null, 0, ref wanted_spec, out spec, (int)(SDL.SDL_AUDIO_ALLOW_FREQUENCY_CHANGE | SDL.SDL_AUDIO_ALLOW_CHANNELS_CHANGE))) == 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"SDL_OpenAudio ({wanted_spec.channels} channels, {wanted_spec.freq} Hz): {SDL.SDL_GetError()}\n");
                wanted_spec.channels = (byte)next_nb_channels[Math.Min(7, (int)wanted_spec.channels)];
                if (wanted_spec.channels == 0)
                {
                    wanted_spec.freq = next_sample_rates[next_sample_rate_idx--];
                    wanted_spec.channels = (byte)wanted_nb_channels;
                    if (wanted_spec.freq == 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "No more combinations to try, audio open failed\n");
                        return -1;
                    }
                }

                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_spec.channels);
            }
            if (spec.format != SDL.AUDIO_S16SYS)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                       $"SDL advised audio format {spec.format} is not supported!\n");
                return -1;
            }
            if (spec.channels != wanted_spec.channels)
            {
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(spec.channels);
                if (wanted_channel_layout == 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"SDL advised channel count {spec.channels} is not supported!\n");
                    return -1;
                }
            }

            audio_hw_params.SampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            audio_hw_params.Frequency = spec.freq;
            audio_hw_params.Layout = wanted_channel_layout;
            audio_hw_params.Channels = spec.channels;
            audio_hw_params.FrameSize = ffmpeg.av_samples_get_buffer_size(null, audio_hw_params.Channels, 1, audio_hw_params.SampleFormat, 1);
            audio_hw_params.BytesPerSecond = ffmpeg.av_samples_get_buffer_size(null, audio_hw_params.Channels, audio_hw_params.Frequency, audio_hw_params.SampleFormat, 1);
            if (audio_hw_params.BytesPerSecond <= 0 || audio_hw_params.FrameSize <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size failed\n");
                return -1;
            }

            return (int)spec.size;
        }

        /* return the wanted number of samples to get better sync if sync_type is video
* or external master clock */
        public int synchronize_audio(int sampleCount)
        {
            var wantedSampleCount = sampleCount;

            /* if not master, then we try to remove or add samples to correct the clock */
            if (MasterSyncMode != ClockSync.Audio)
            {
                double diff, avg_diff;
                int min_nb_samples, max_nb_samples;

                diff = AudioClock.Time - MasterTime;

                if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD)
                {
                    audio_diff_cum = diff + audio_diff_avg_coef * audio_diff_cum;
                    if (audio_diff_avg_count < Constants.AUDIO_DIFF_AVG_NB)
                    {
                        /* not enough measures to have a correct estimate */
                        audio_diff_avg_count++;
                    }
                    else
                    {
                        /* estimate the A-V difference */
                        avg_diff = audio_diff_cum * (1.0 - audio_diff_avg_coef);

                        if (Math.Abs(avg_diff) >= audio_diff_threshold)
                        {
                            wantedSampleCount = sampleCount + (int)(diff * Audio.SourceSpec.Frequency);
                            min_nb_samples = (int)((sampleCount * (100 - Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            max_nb_samples = (int)((sampleCount * (100 + Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            wantedSampleCount = Helpers.av_clip(wantedSampleCount, min_nb_samples, max_nb_samples);
                        }

                        ffmpeg.av_log(
                            null, ffmpeg.AV_LOG_TRACE, $"diff={diff} adiff={avg_diff} sample_diff={(wantedSampleCount - sampleCount)} apts={audio_clock} {audio_diff_threshold}\n");
                    }
                }
                else
                {
                    /* too big difference : may be initial PTS errors, so
                       reset A-V filter */
                    audio_diff_avg_count = 0;
                    audio_diff_cum = 0;
                }
            }

            return wantedSampleCount;
        }


        /* this thread gets the stream from the disk or the network */
        private void read_thread()
        {
            var o = Options;
            AVFormatContext* ic = null;
            int err, i, ret;
            var st_index = new int[(int)AVMediaType.AVMEDIA_TYPE_NB];
            // AVPacket pkt1;
            // AVPacket* pkt = &pkt1;
            long stream_start_time;
            bool pkt_in_play_range = false;
            AVDictionaryEntry* t;
            bool scan_all_pmts_set = false;
            long pkt_ts;

            for (var b = 0; b < st_index.Length; b++)
                st_index[b] = -1;

            eof = false;

            ic = ffmpeg.avformat_alloc_context();
            if (ic == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Could not allocate context.\n");
                ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                goto fail;
            }

            ic->interrupt_callback.callback = (AVIOInterruptCB_callback)decode_interrupt_cb;
            // ic->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(VideoStateHandle);
            if (ffmpeg.av_dict_get(o.format_opts, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE) == null)
            {
                var formatOptions = o.format_opts;
                ffmpeg.av_dict_set(&formatOptions, "scan_all_pmts", "1", ffmpeg.AV_DICT_DONT_OVERWRITE);
                o.format_opts = formatOptions;
                scan_all_pmts_set = true;
            }

            {
                var formatOptions = o.format_opts;
                err = ffmpeg.avformat_open_input(&ic, filename, iformat, &formatOptions);
                o.format_opts = formatOptions;
            }

            if (err < 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"{filename}: {Helpers.print_error(err)}\n");
                ret = -1;
                goto fail;
            }
            if (scan_all_pmts_set)
            {
                var formatOptions = o.format_opts;
                ffmpeg.av_dict_set(&formatOptions, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE);
                o.format_opts = formatOptions;
            }

            if ((t = ffmpeg.av_dict_get(o.format_opts, "", null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {Helpers.PtrToString(t->key)} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            InputContext = ic;

            if (o.genpts)
                ic->flags |= ffmpeg.AVFMT_FLAG_GENPTS;

            ffmpeg.av_format_inject_global_side_data(ic);

            if (o.find_stream_info)
            {
                AVDictionary** opts = Helpers.setup_find_stream_info_opts(ic, o.codec_opts);
                int orig_nb_streams = (int)ic->nb_streams;

                err = ffmpeg.avformat_find_stream_info(ic, opts);

                for (i = 0; i < orig_nb_streams; i++)
                    ffmpeg.av_dict_free(&opts[i]);

                ffmpeg.av_freep(&opts);

                if (err < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"{filename}: could not find codec parameters\n");
                    ret = -1;
                    goto fail;
                }
            }

            if (ic->pb != null)
                ic->pb->eof_reached = 0; // FIXME hack, ffplay maybe should not use avio_feof() to test for the end

            if (o.seek_by_bytes < 0)
                o.seek_by_bytes = ((ic->iformat->flags & ffmpeg.AVFMT_TS_DISCONT) > 0 && Helpers.PtrToString(ic->iformat->name) != "ogg") ? 1 : 0;

            max_frame_duration = (ic->iformat->flags & ffmpeg.AVFMT_TS_DISCONT) != 0 ? 10.0 : 3600.0;

            if (string.IsNullOrWhiteSpace(Renderer.window_title) && (t = ffmpeg.av_dict_get(ic->metadata, "title", null, 0)) != null)
                Renderer.window_title = $"{Helpers.PtrToString(t->value)} - {o.input_filename}";

            /* if seeking requested, we execute it */
            if (o.start_time != ffmpeg.AV_NOPTS_VALUE)
            {
                long timestamp;

                timestamp = o.start_time;
                /* add the stream start time */
                if (ic->start_time != ffmpeg.AV_NOPTS_VALUE)
                    timestamp += ic->start_time;
                ret = ffmpeg.avformat_seek_file(ic, -1, long.MinValue, timestamp, long.MaxValue, 0);
                if (ret < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"{filename}: could not seek to position {((double)timestamp / ffmpeg.AV_TIME_BASE)}\n");
                }
            }

            realtime = is_realtime(ic);

            if (o.show_status != 0)
                ffmpeg.av_dump_format(ic, 0, filename, 0);

            for (i = 0; i < ic->nb_streams; i++)
            {
                AVStream* st = ic->streams[i];
                var type = (int)st->codecpar->codec_type;
                st->discard = AVDiscard.AVDISCARD_ALL;
                if (type >= 0 && o.wanted_stream_spec[type] != null && st_index[type] == -1)
                    if (ffmpeg.avformat_match_stream_specifier(ic, st, o.wanted_stream_spec[type]) > 0)
                        st_index[type] = i;
            }
            for (i = 0; i < (int)AVMediaType.AVMEDIA_TYPE_NB; i++)
            {
                if (o.wanted_stream_spec[i] != null && st_index[i] == -1)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Stream specifier {Options.wanted_stream_spec[i]} does not match any {ffmpeg.av_get_media_type_string((AVMediaType)i)} stream\n");
                    st_index[i] = int.MaxValue;
                }
            }

            if (!o.video_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_VIDEO,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO], -1, null, 0);
            if (!o.audio_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_AUDIO,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO],
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO],
                                        null, 0);
            if (!o.video_disable && !o.subtitle_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_SUBTITLE,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE],
                                        (st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0 ?
                                         st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] :
                                         st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]),
                                        null, 0);

            show_mode = o.show_mode;
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                AVStream* st = ic->streams[st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]];
                AVCodecParameters* codecpar = st->codecpar;
                AVRational sar = ffmpeg.av_guess_sample_aspect_ratio(ic, st, null);
                if (codecpar->width != 0)
                    Renderer.set_default_window_size(codecpar->width, codecpar->height, sar);
            }

            /* open the streams */
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0)
            {
                stream_component_open(st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO]);
            }

            ret = -1;
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                ret = stream_component_open(st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]);
            }

            if (show_mode == ShowMode.None)
                show_mode = ret >= 0 ? ShowMode.Video : ShowMode.Rdft;

            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE] >= 0)
            {
                stream_component_open(st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE]);
            }

            if (Video.StreamIndex < 0 && Audio.StreamIndex < 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Failed to open file '{filename}' or configure filtergraph\n");
                ret = -1;
                goto fail;
            }

            if (o.infinite_buffer < 0 && realtime)
                o.infinite_buffer = 1;

            while (true)
            {
                if (abort_request)
                    break;
                if (paused != last_paused)
                {
                    last_paused = paused;
                    if (paused)
                        read_pause_return = ffmpeg.av_read_pause(ic);
                    else
                        ffmpeg.av_read_play(ic);
                }

                if (paused &&
                        (Helpers.PtrToString(ic->iformat->name) == "rtsp" ||
                         (ic->pb != null && o.input_filename.StartsWith("mmsh:"))))
                {
                    /* wait 10 ms to avoid trying to get another packet */
                    /* XXX: horrible */
                    SDL.SDL_Delay(10);
                    continue;
                }

                if (seek_req)
                {
                    var seek_target = seek_pos;
                    var seek_min = seek_rel > 0 ? seek_target - seek_rel + 2 : long.MinValue;
                    var seek_max = seek_rel < 0 ? seek_target - seek_rel - 2 : long.MaxValue;
                    // FIXME the +-2 is due to rounding being not done in the correct direction in generation
                    //      of the seek_pos/seek_rel variables

                    ret = ffmpeg.avformat_seek_file(InputContext, -1, seek_min, seek_target, seek_max, seek_flags);
                    if (ret < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"{Helpers.PtrToString(InputContext->url)}: error while seeking\n");
                    }
                    else
                    {
                        if (Audio.StreamIndex >= 0)
                        {
                            Audio.Packets.Clear();
                            Audio.Packets.PutFlush();
                        }
                        if (Subtitle.StreamIndex >= 0)
                        {
                            Subtitle.Packets.Clear();
                            Subtitle.Packets.PutFlush();
                        }
                        if (Video.StreamIndex >= 0)
                        {
                            Video.Packets.Clear();
                            Video.Packets.PutFlush();
                        }
                        if ((seek_flags & ffmpeg.AVSEEK_FLAG_BYTE) != 0)
                        {
                            ExternalClock.Set(double.NaN, 0);
                        }
                        else
                        {
                            ExternalClock.Set(seek_target / (double)ffmpeg.AV_TIME_BASE, 0);
                        }
                    }
                    seek_req = false;
                    queue_attachments_req = true;
                    eof = false;

                    if (paused)
                        step_to_next_frame();
                }
                if (queue_attachments_req)
                {
                    if (Video.Stream != null && (Video.Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0)
                    {
                        var copy = ffmpeg.av_packet_clone(&Video.Stream->attached_pic);
                        Video.Packets.Put(copy);
                        Video.Packets.PutNull();
                    }

                    queue_attachments_req = false;
                }

                /* if the queue are full, no need to read more */
                if (o.infinite_buffer < 1 &&
                      (Audio.Packets.Size + Video.Packets.Size + Subtitle.Packets.Size > Constants.MAX_QUEUE_SIZE
                    || (Audio.HasEnoughPackets &&
                        Video.HasEnoughPackets &&
                        Subtitle.HasEnoughPackets)))
                {
                    /* wait 10 ms */
                    continue_read_thread.WaitOne(10);
                    continue;
                }
                if (!paused &&
                    (Audio.Stream == null || (Audio.Decoder.HasFinished == Audio.Packets.Serial && Audio.Frames.PendingCount == 0)) &&
                    (Video.Stream == null || (Video.Decoder.HasFinished == Video.Packets.Serial && Video.Frames.PendingCount == 0)))
                {
                    if (o.loop != 1 && (o.loop == 0 || (--o.loop) > 0))
                    {
                        stream_seek(o.start_time != ffmpeg.AV_NOPTS_VALUE ? o.start_time : 0, 0, 0);
                    }
                    else if (o.autoexit)
                    {
                        ret = ffmpeg.AVERROR_EOF;
                        goto fail;
                    }
                }

                var pkt = ffmpeg.av_packet_alloc();
                ret = ffmpeg.av_read_frame(ic, pkt);
                if (ret < 0)
                {
                    if ((ret == ffmpeg.AVERROR_EOF || ffmpeg.avio_feof(ic->pb) != 0) && !eof)
                    {
                        if (Video.StreamIndex >= 0)
                            Video.Packets.PutNull();
                        if (Audio.StreamIndex >= 0)
                            Audio.Packets.PutNull();
                        if (Subtitle.StreamIndex >= 0)
                            Subtitle.Packets.PutNull();
                        eof = true;
                    }
                    if (ic->pb != null && ic->pb->error != 0)
                    {
                        if (o.autoexit)
                            goto fail;
                        else
                            break;
                    }

                    continue_read_thread.WaitOne(10);

                    continue;
                }
                else
                {
                    eof = false;
                }

                /* check if packet is in play range specified by user, then queue, otherwise discard */
                stream_start_time = ic->streams[pkt->stream_index]->start_time;
                pkt_ts = pkt->pts == ffmpeg.AV_NOPTS_VALUE ? pkt->dts : pkt->pts;
                pkt_in_play_range = o.duration == ffmpeg.AV_NOPTS_VALUE ||
                        (pkt_ts - (stream_start_time != ffmpeg.AV_NOPTS_VALUE ? stream_start_time : 0)) *
                        ffmpeg.av_q2d(ic->streams[pkt->stream_index]->time_base) -
                        (double)(o.start_time != ffmpeg.AV_NOPTS_VALUE ? o.start_time : 0) / 1000000
                        <= ((double)o.duration / 1000000);
                if (pkt->stream_index == Audio.StreamIndex && pkt_in_play_range)
                {
                    Audio.Packets.Put(pkt);
                }
                else if (pkt->stream_index == Video.StreamIndex && pkt_in_play_range
                         && (Video.Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                {
                    Video.Packets.Put(pkt);
                }
                else if (pkt->stream_index == Subtitle.StreamIndex && pkt_in_play_range)
                {
                    Subtitle.Packets.Put(pkt);
                }
                else
                {
                    ffmpeg.av_packet_unref(pkt);
                }
            }

            ret = 0;
        fail:
            if (ic != null && InputContext == null)
                ffmpeg.avformat_close_input(&ic);

            if (ret != 0)
            {
                SDL.SDL_Event evt = new();
                evt.type = (SDL.SDL_EventType)Constants.FF_QUIT_EVENT;
                // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                SDL.SDL_PushEvent(ref evt);
            }

            return; // 0;
        }
    }
}
