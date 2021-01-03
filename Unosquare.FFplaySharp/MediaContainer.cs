namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Unosquare.FFplaySharp.Components;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class MediaContainer
    {
        private readonly AVIOInterruptCB_callback InputInterruptCallback;
        private bool WasPaused;
        private bool IsPictureAttachmentPending;
        public bool IsSeekRequested { get; private set; }

        private int SeekFlags;
        private Thread ReadingThread;
        private AVInputFormat* InputFormat = null;

        public bool IsAbortRequested { get; private set; }

        public bool IsPaused { get; private set; }

        public long SeekPosition { get; private set; }

        private long seek_rel;
        private int read_pause_return;

        public AVFormatContext* InputContext { get; private set; }

        public bool IsRealtime { get; private set; }

        public Clock AudioClock { get; private set; }

        public Clock VideoClock { get; private set; }

        public Clock ExternalClock { get; private set; }

        public AudioComponent Audio { get; }

        public VideoComponent Video { get; }

        public SubtitleComponent Subtitle { get; }

        public ClockSync ClockSyncMode { get; private set; }



        public int audio_hw_buf_size;

        
        


        public bool IsMuted { get; private set; }
        public int frame_drops_early;
        public int frame_drops_late;

        public ShowMode show_mode;

        public short* sample_array;
        public int sample_array_index;
        public int last_i_start;
        // RDFTContext* rdft;
        // int rdft_bits;
        // FFTSample* rdft_data;
        // public int xpos;
        public double last_vis_time;

        public double frame_timer;


        public double max_frame_duration;      // maximum duration of a frame - above this, we consider the jump a timestamp discontinuity

        public bool IsAtEndOfStream { get; private set; }

        public string filename;
        public int width = 1;
        public int height = 1;
        public int xleft;
        public int ytop;
        public bool step;

        public int vfilter_idx;
        public MediaRenderer Renderer { get; }

        public AutoResetEvent NeedsMorePacketsEvent = new(true);

        private MediaContainer(ProgramOptions options, MediaRenderer renderer)
        {
            InputInterruptCallback = new(InputInterrupt);
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
                return MasterSyncMode switch
                {
                    ClockSync.Video => VideoClock.Time,
                    ClockSync.Audio => AudioClock.Time,
                    _ => ExternalClock.Time,
                };
            }
        }

        public IReadOnlyList<MediaComponent> Components { get; }


        public static MediaContainer stream_open(ProgramOptions options, MediaRenderer renderer)
        {
            var container = new MediaContainer(options, renderer);

            var o = container.Options;
            container.Video.LastStreamIndex = container.Video.StreamIndex = -1;
            container.Audio.LastStreamIndex = container.Audio.StreamIndex = -1;
            container.Subtitle.LastStreamIndex = container.Subtitle.StreamIndex = -1;
            container.filename = o.input_filename;
            if (string.IsNullOrWhiteSpace(container.filename))
                goto fail;

            container.InputFormat = o.file_iformat;
            container.ytop = 0;
            container.xleft = 0;

            container.VideoClock = new Clock(container.Video.Packets);
            container.AudioClock = new Clock(container.Audio.Packets);
            container.ExternalClock = new Clock(container.ExternalClock);

            if (container.Options.startup_volume < 0)
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"-volume={container.Options.startup_volume} < 0, setting to 0\n");

            if (container.Options.startup_volume > 100)
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"-volume={container.Options.startup_volume} > 100, setting to 100\n");

            container.Options.startup_volume = container.Options.startup_volume.Clamp(0, 100);
            container.Options.startup_volume = (SDL.SDL_MIX_MAXVOLUME * container.Options.startup_volume / 100).Clamp(0, SDL.SDL_MIX_MAXVOLUME);
            renderer.audio_volume = container.Options.startup_volume;
            container.IsMuted = false;
            container.ClockSyncMode = container.Options.av_sync_type;
            container.StartReadThread();
            return container;

        fail:
            container.stream_close();
            return null;
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

        public void toggle_mute()
        {
            IsMuted = !IsMuted;
        }

        public void toggle_pause()
        {
            stream_toggle_pause();
            step = false;
        }

        public void stream_toggle_pause()
        {
            if (IsPaused)
            {
                frame_timer += Clock.SystemTime - VideoClock.LastUpdated;
                if (read_pause_return != ffmpeg.AVERROR(38))
                {
                    VideoClock.IsPaused = false;
                }
                VideoClock.Set(VideoClock.Time, VideoClock.Serial);
            }

            ExternalClock.Set(ExternalClock.Time, ExternalClock.Serial);
            IsPaused = AudioClock.IsPaused = VideoClock.IsPaused = ExternalClock.IsPaused = !IsPaused;
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
            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"Switch {component.MediaTypeString} stream from #{component.StreamIndex} to #{nextStreamIndex}\n");

            component.Close();
            OpenComponent(nextStreamIndex);
        }

        public void StartReadThread()
        {
            ReadingThread = new Thread(ReadingThreadMethod) { IsBackground = true, Name = nameof(ReadingThreadMethod) };
            ReadingThread.Start();
        }

        private int InputInterrupt(void* opaque)
        {
            return IsAbortRequested ? 1 : 0;
        }

        private static bool IsInputFormatRealtime(AVFormatContext* ic)
        {
            var inputFormatName = Helpers.PtrToString(ic->iformat->name);
            if (inputFormatName == "rtp" || inputFormatName == "rtsp" || inputFormatName == "sdp")
                return true;

            var url = Helpers.PtrToString(ic->url)?.ToLowerInvariant();
            url = string.IsNullOrEmpty(url) ? string.Empty : url;

            if (ic->pb != null && (url.StartsWith("rtp:") || url.StartsWith("udp:")))
                return true;

            return false;
        }

        public void step_to_next_frame()
        {
            /* if the stream is paused unpause it, then step */
            if (IsPaused)
                stream_toggle_pause();
            step = true;
        }

        /* seek in the stream */
        public void stream_seek(long pos, long rel, bool seekByBytes)
        {
            if (IsSeekRequested)
                return;

            SeekPosition = pos;
            seek_rel = rel;
            SeekFlags &= ~ffmpeg.AVSEEK_FLAG_BYTE;
            if (seekByBytes)
                SeekFlags |= ffmpeg.AVSEEK_FLAG_BYTE;

            IsSeekRequested = true;
            NeedsMorePacketsEvent.Set();
        }

        public void seek_chapter(int incrementCount)
        {
            var i = 0;
            var pos = (long)(MasterTime * Clock.TimeBaseMicros);

            if (InputContext->nb_chapters <= 0)
                return;

            /* find the current chapter */
            for (i = 0; i < InputContext->nb_chapters; i++)
            {
                var chapter = InputContext->chapters[i];
                if (ffmpeg.av_compare_ts(pos, Constants.AV_TIME_BASE_Q, chapter->start, chapter->time_base) < 0)
                {
                    i--;
                    break;
                }
            }

            i += incrementCount;
            i = Math.Max(i, 0);
            if (i >= InputContext->nb_chapters)
                return;

            ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Seeking to chapter {i}.\n");
            stream_seek(ffmpeg.av_rescale_q(InputContext->chapters[i]->start, InputContext->chapters[i]->time_base, Constants.AV_TIME_BASE_Q), 0, false);
        }

        public bool IsSeekMethodUnknown =>
            InputContext != null &&
            InputContext->iformat != null &&
            InputContext->iformat->flags.HasFlag(Constants.SeekMethodUnknownFlags) &&
            InputContext->iformat->read_seek.Pointer.IsNull();

        /// <summary>
        /// Open a given stream index. Returns 0 if OK.
        /// Port of stream_component_open
        /// </summary>
        /// <param name="streamIndex"></param>
        /// <returns>0 if OK</returns>
        public int OpenComponent(int streamIndex)
        {
            var ic = InputContext;
            var ret = 0;
            var lowResFactor = Options.lowres;

            if (streamIndex < 0 || streamIndex >= ic->nb_streams)
                return -1;

            var codecContext = ffmpeg.avcodec_alloc_context3(null);
            ret = ffmpeg.avcodec_parameters_to_context(codecContext, ic->streams[streamIndex]->codecpar);

            if (ret < 0) goto fail;
            codecContext->pkt_timebase = ic->streams[streamIndex]->time_base;

            var codec = ffmpeg.avcodec_find_decoder(codecContext->codec_id);
            var targetMediaType = codecContext->codec_type;

            var targetComponent = targetMediaType switch
            {
                AVMediaType.AVMEDIA_TYPE_AUDIO => Audio as MediaComponent,
                AVMediaType.AVMEDIA_TYPE_VIDEO => Video,
                AVMediaType.AVMEDIA_TYPE_SUBTITLE => Subtitle,
                _ => throw new NotSupportedException($"Opening '{targetMediaType}' is not supported.")
            };

            var forcedCodecName = targetMediaType switch
            {
                AVMediaType.AVMEDIA_TYPE_AUDIO => Options.AudioForcedCodecName,
                AVMediaType.AVMEDIA_TYPE_VIDEO => Options.VideoForcedCodecName,
                AVMediaType.AVMEDIA_TYPE_SUBTITLE => Options.SubtitleForcedCodecName,
                _ => null
            };

            targetComponent.LastStreamIndex = streamIndex;

            if (!string.IsNullOrWhiteSpace(forcedCodecName))
                codec = ffmpeg.avcodec_find_decoder_by_name(forcedCodecName);

            if (codec == null)
            {
                if (!string.IsNullOrWhiteSpace(forcedCodecName))
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"No codec could be found with name '{forcedCodecName}'\n");
                else
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"No decoder could be found for codec {ffmpeg.avcodec_get_name(codecContext->codec_id)}\n");
                ret = ffmpeg.AVERROR(ffmpeg.EINVAL);
                goto fail;
            }

            codecContext->codec_id = codec->id;
            if (lowResFactor > codec->max_lowres)
            {
                ffmpeg.av_log(codecContext, ffmpeg.AV_LOG_WARNING, $"The maximum value for lowres supported by the decoder is {codec->max_lowres}\n");
                lowResFactor = codec->max_lowres;
            }

            codecContext->lowres = lowResFactor;

            if (Options.fast != 0)
                codecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            const string ThreadsOptionKey = "threads";
            const string ThreadsOptionValue = "auto";

            var codecOptions = Helpers.filter_codec_opts(Options.codec_opts, codecContext->codec_id, ic, ic->streams[streamIndex], codec);
            if (ffmpeg.av_dict_get(codecOptions, ThreadsOptionKey, null, 0) == null)
                ffmpeg.av_dict_set(&codecOptions, ThreadsOptionKey, ThreadsOptionValue, 0);

            if (lowResFactor != 0)
                ffmpeg.av_dict_set_int(&codecOptions, "lowres", lowResFactor, 0);

            if (targetMediaType == AVMediaType.AVMEDIA_TYPE_VIDEO || targetMediaType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                ffmpeg.av_dict_set(&codecOptions, "refcounted_frames", "1", 0);

            if ((ret = ffmpeg.avcodec_open2(codecContext, codec, &codecOptions)) < 0)
            {
                goto fail;
            }

            var t = ffmpeg.av_dict_get(codecOptions, string.Empty, null, ffmpeg.AV_DICT_IGNORE_SUFFIX);
            if (t != null)
            {
                var key = Helpers.PtrToString(t->key);
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {key} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            IsAtEndOfStream = false;
            ic->streams[streamIndex]->discard = AVDiscard.AVDISCARD_DEFAULT;

            switch (targetMediaType)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    Audio.FilterSpec.ImportFrom(codecContext);
                    if ((ret = Audio.ConfigureFilters(false)) < 0)
                        goto fail;

                    var wantedSpec = AudioParams.FromFilterContext(Audio.OutputFilter);
                    ret = Renderer.audio_open(wantedSpec, out var audioHardwareSpec);

                    // prepare audio output
                    if (ret < 0)
                        goto fail;

                    audio_hw_buf_size = ret;
                    
                    Audio.HardwareSpec = audioHardwareSpec.Clone();
                    Audio.SourceSpec = audioHardwareSpec.Clone();

                    Audio.InitializeDecoder(codecContext, streamIndex);
                    Audio.Start();
                    Renderer.PauseAudio();
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:

                    targetComponent.InitializeDecoder(codecContext, streamIndex);
                    targetComponent.Start();

                    if (targetComponent.IsVideo)
                        IsPictureAttachmentPending = true;

                    break;
                default:
                    break;
            }
            goto exit;

        fail:
            ffmpeg.avcodec_free_context(&codecContext);
        exit:
            ffmpeg.av_dict_free(&codecOptions);

            return ret;
        }

        public void stream_close()
        {
            // TODO: Use a special url_shutdown call to abort parse cleanly.
            IsAbortRequested = true;
            ReadingThread.Join();

            // Close each component.
            foreach (var component in Components)
                component.Close();

            // Close the input context.
            var ic = InputContext;
            ffmpeg.avformat_close_input(&ic);
            InputContext = null;

            // Release packets and frames
            foreach (var component in Components)
            {
                component.Packets?.Dispose();
                component.Frames?.Dispose();
            }

            NeedsMorePacketsEvent.Dispose();
            ffmpeg.sws_freeContext(Video.ConvertContext);
            ffmpeg.sws_freeContext(Subtitle.ConvertContext);

            ffmpeg.av_free(sample_array);
            sample_array = null;
        }

        private bool HasEnoughPacketCount => Components.All(c => c.HasEnoughPackets);

        private long PacketQueueSize => Components.Sum(c => c.Packets.Size);

        private bool HasEnoughPacketSize => PacketQueueSize > Constants.MAX_QUEUE_SIZE;

        private MediaComponent ComponentFromStreamIndex(int streamIndex)
        {
            foreach (var c in Components)
            {
                if (c.StreamIndex == streamIndex)
                    return c;
            }

            return null;
        }

        /* this thread gets the stream from the disk or the network */
        private void ReadingThreadMethod()
        {
            const int MediaTypeCount = (int)AVMediaType.AVMEDIA_TYPE_NB;

            var o = Options;
            int err, i, ret;
            var streamIndexes = new Dictionary<AVMediaType, int>(MediaTypeCount);
            bool scan_all_pmts_set = false;

            for (var mediaType = 0; mediaType < MediaTypeCount; mediaType++)
                streamIndexes[(AVMediaType)mediaType] = -1;

            IsAtEndOfStream = false;

            var ic = ffmpeg.avformat_alloc_context();
            ic->interrupt_callback.callback = InputInterruptCallback;

            if (ffmpeg.av_dict_get(o.format_opts, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE) == null)
            {
                var formatOptions = o.format_opts;
                ffmpeg.av_dict_set(&formatOptions, "scan_all_pmts", "1", ffmpeg.AV_DICT_DONT_OVERWRITE);
                o.format_opts = formatOptions;
                scan_all_pmts_set = true;
            }

            {
                var formatOptions = o.format_opts;
                err = ffmpeg.avformat_open_input(&ic, filename, InputFormat, &formatOptions);
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

            AVDictionaryEntry* formatOption;
            if ((formatOption = ffmpeg.av_dict_get(o.format_opts, string.Empty, null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {Helpers.PtrToString(formatOption->key)} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            InputContext = ic;

            if (o.genpts)
                ic->flags |= ffmpeg.AVFMT_FLAG_GENPTS;

            ffmpeg.av_format_inject_global_side_data(ic);

            if (o.find_stream_info)
            {
                var opts = Helpers.setup_find_stream_info_opts(ic, o.codec_opts);
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

            if (o.seek_by_bytes.IsAuto())
                o.seek_by_bytes = ic->iformat->flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) && Helpers.PtrToString(ic->iformat->name) != "ogg" ? 1 : 0;

            max_frame_duration = ic->iformat->flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) ? 10.0 : 3600.0;

            if (string.IsNullOrWhiteSpace(Renderer.window_title) && (formatOption = ffmpeg.av_dict_get(ic->metadata, "title", null, 0)) != null)
                Renderer.window_title = $"{Helpers.PtrToString(formatOption->value)} - {o.input_filename}";

            /* if seeking requested, we execute it */
            if (o.start_time.IsValidPts())
            {
                var timestamp = o.start_time;
                /* add the stream start time */
                if (ic->start_time.IsValidPts())
                    timestamp += ic->start_time;

                ret = ffmpeg.avformat_seek_file(ic, -1, long.MinValue, timestamp, long.MaxValue, 0);
                if (ret < 0)
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"{filename}: could not seek to position {(timestamp / Clock.TimeBaseMicros)}\n");
            }

            IsRealtime = IsInputFormatRealtime(ic);

            if (o.show_status != 0)
                ffmpeg.av_dump_format(ic, 0, filename, 0);

            for (i = 0; i < ic->nb_streams; i++)
            {
                var st = ic->streams[i];
                var type = st->codecpar->codec_type;
                st->discard = AVDiscard.AVDISCARD_ALL;
                if (type >= 0 && o.wanted_stream_spec[(int)type] != null && streamIndexes[type] == -1)
                    if (ffmpeg.avformat_match_stream_specifier(ic, st, o.wanted_stream_spec[(int)type]) > 0)
                        streamIndexes[type] = i;
            }

            for (i = 0; i < (int)AVMediaType.AVMEDIA_TYPE_NB; i++)
            {
                if (o.wanted_stream_spec[i] != null && streamIndexes[(AVMediaType)i] == -1)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Stream specifier {Options.wanted_stream_spec[i]} does not match any {ffmpeg.av_get_media_type_string((AVMediaType)i)} stream\n");
                    streamIndexes[(AVMediaType)i] = int.MaxValue;
                }
            }

            if (!o.video_disable)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_VIDEO,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO], -1, null, 0);
            if (!o.audio_disable)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_AUDIO,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO],
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO],
                                        null, 0);
            if (!o.video_disable && !o.subtitle_disable)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_SUBTITLE,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE],
                                        (streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0 ?
                                         streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] :
                                         streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO]),
                                        null, 0);

            show_mode = o.show_mode;
            if (streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                AVStream* st = ic->streams[streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO]];
                AVCodecParameters* codecpar = st->codecpar;
                AVRational sar = ffmpeg.av_guess_sample_aspect_ratio(ic, st, null);
                if (codecpar->width != 0)
                    Renderer.set_default_window_size(codecpar->width, codecpar->height, sar);
            }

            /* open the streams */
            if (streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0)
            {
                OpenComponent(streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO]);
            }

            ret = -1;
            if (streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                ret = OpenComponent(streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO]);
            }

            if (show_mode == ShowMode.None)
                show_mode = ret >= 0 ? ShowMode.Video : ShowMode.None;

            if (streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE] >= 0)
            {
                OpenComponent(streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE]);
            }

            if (Video.StreamIndex < 0 && Audio.StreamIndex < 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Failed to open file '{filename}' or configure filtergraph\n");
                ret = -1;
                goto fail;
            }

            if (o.infinite_buffer < 0 && IsRealtime)
                o.infinite_buffer = 1;

            while (true)
            {
                if (IsAbortRequested)
                    break;

                if (IsPaused != WasPaused)
                {
                    WasPaused = IsPaused;
                    if (IsPaused)
                        read_pause_return = ffmpeg.av_read_pause(ic);
                    else
                        ffmpeg.av_read_play(ic);
                }

                if (IsPaused &&
                        (Helpers.PtrToString(ic->iformat->name) == "rtsp" ||
                         (ic->pb != null && o.input_filename.StartsWith("mmsh:"))))
                {
                    /* wait 10 ms to avoid trying to get another packet */
                    /* XXX: horrible */
                    SDL.SDL_Delay(10);
                    continue;
                }

                if (IsSeekRequested)
                {
                    var seek_target = SeekPosition;
                    var seek_min = seek_rel > 0 ? seek_target - seek_rel + 2 : long.MinValue;
                    var seek_max = seek_rel < 0 ? seek_target - seek_rel - 2 : long.MaxValue;
                    // FIXME the +-2 is due to rounding being not done in the correct direction in generation
                    //      of the seek_pos/seek_rel variables

                    ret = ffmpeg.avformat_seek_file(InputContext, -1, seek_min, seek_target, seek_max, SeekFlags);
                    if (ret < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"{Helpers.PtrToString(InputContext->url)}: error while seeking\n");
                    }
                    else
                    {
                        foreach (var c in Components)
                        {
                            if (c.StreamIndex < 0)
                                continue;

                            c.Packets.Clear();
                            c.Packets.PutFlush();
                        }

                        ExternalClock.Set(SeekFlags.HasFlag(ffmpeg.AVSEEK_FLAG_BYTE)
                            ? double.NaN
                            : seek_target / Clock.TimeBaseMicros, 0);
                    }

                    IsSeekRequested = false;
                    IsPictureAttachmentPending = true;
                    IsAtEndOfStream = false;

                    if (IsPaused)
                        step_to_next_frame();
                }

                if (IsPictureAttachmentPending)
                {
                    if (Video.IsPictureAttachmentStream)
                    {
                        var copy = ffmpeg.av_packet_clone(&Video.Stream->attached_pic);
                        Video.Packets.Put(copy);
                        Video.Packets.PutNull();
                    }

                    IsPictureAttachmentPending = false;
                }

                /* if the queue are full, no need to read more */
                if (o.infinite_buffer < 1 && (HasEnoughPacketSize || HasEnoughPacketCount))
                {
                    /* wait 10 ms */
                    NeedsMorePacketsEvent.WaitOne(1);
                    continue;
                }
                if (!IsPaused &&
                    (Audio.Stream == null || (Audio.HasFinished == Audio.Packets.Serial && Audio.Frames.PendingCount == 0)) &&
                    (Video.Stream == null || (Video.HasFinished == Video.Packets.Serial && Video.Frames.PendingCount == 0)))
                {
                    if (o.loop != 1 && (o.loop == 0 || (--o.loop) > 0))
                    {
                        stream_seek(o.start_time.IsValidPts() ? o.start_time : 0, 0, false);
                    }
                    else if (o.autoexit)
                    {
                        ret = ffmpeg.AVERROR_EOF;
                        goto fail;
                    }
                }

                var readPacket = ffmpeg.av_packet_alloc();
                ret = ffmpeg.av_read_frame(ic, readPacket);

                if (ret < 0)
                {
                    if ((ret == ffmpeg.AVERROR_EOF || ffmpeg.avio_feof(ic->pb) != 0) && !IsAtEndOfStream)
                    {
                        foreach (var c in Components)
                        {
                            if (c.StreamIndex >= 0)
                                c.Packets.PutNull();
                        }

                        IsAtEndOfStream = true;
                    }

                    if (ic->pb != null && ic->pb->error != 0)
                    {
                        if (o.autoexit)
                            goto fail;
                        else
                            break;
                    }

                    NeedsMorePacketsEvent.WaitOne(1);
                    continue;
                }
                else
                {
                    IsAtEndOfStream = false;
                }

                // check if packet is in play range specified by user, then queue, otherwise discard.
                var streamStartPts = ic->streams[readPacket->stream_index]->start_time;
                var packetPts = readPacket->pts.IsValidPts()
                    ? readPacket->pts
                    : readPacket->dts;

                var isPacketInPlayRange = !o.duration.IsValidPts() ||
                        (packetPts - (streamStartPts.IsValidPts() ? streamStartPts : 0)) *
                        ffmpeg.av_q2d(ic->streams[readPacket->stream_index]->time_base) -
                        (o.start_time.IsValidPts() ? o.start_time : 0) / Clock.TimeBaseMicros
                        <= (o.duration / Clock.TimeBaseMicros);

                var component = ComponentFromStreamIndex(readPacket->stream_index);
                if (component != null && !component.IsPictureAttachmentStream && isPacketInPlayRange)
                    component.Packets.Put(readPacket);
                else
                    ffmpeg.av_packet_unref(readPacket);
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
