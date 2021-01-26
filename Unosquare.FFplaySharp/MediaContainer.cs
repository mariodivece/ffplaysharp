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
    using Unosquare.FFplaySharp.Rendering;

    public unsafe class MediaContainer
    {
        private readonly AVIOInterruptCB_callback InputInterruptCallback;

        private AVInputFormat* InputFormat = null;
        private Thread ReadingThread;

        private bool WasPaused;
        private bool IsPictureAttachmentPending;
        private int SeekFlags;
        private long SeekRelativeTarget;

        private int ReadPauseResultCode;

        public ShowMode ShowMode { get; set; }
        public int width;
        public int height;
        public int xleft;
        public int ytop;

        private MediaContainer(ProgramOptions options, IPresenter renderer)
        {
            InputInterruptCallback = new(InputInterrupt);
            Options = options ?? new();
            Audio = new(this);
            Video = new(this);
            Subtitle = new(this);
            Renderer = renderer;
            Components = new List<MediaComponent>() { Audio, Video, Subtitle };
        }

        public AVFormatContext* InputContext { get; private set; }

        public IPresenter Renderer { get; }

        public ProgramOptions Options { get; }

        public AutoResetEvent NeedsMorePacketsEvent { get; } = new(true);

        public string FileName { get; private set; }

        public bool IsAbortRequested { get; private set; }

        public bool IsSeekRequested { get; private set; }

        public bool IsSeekMethodUnknown =>
            InputContext != null &&
            InputContext->iformat != null &&
            InputContext->iformat->flags.HasFlag(Constants.SeekMethodUnknownFlags) &&
            InputContext->iformat->read_seek.Pointer.IsNull();

        public long SeekAbsoluteTarget { get; private set; }

        public bool IsInStepMode { get; private set; }

        public bool IsPaused { get; private set; }

        public bool IsMuted { get; private set; }

        public bool IsRealtime { get; private set; }

        public long StreamBytePosition
        {
            get
            {
                var bytePosition = -1L;

                if (bytePosition < 0 && HasVideo)
                    bytePosition = Video.Frames.LastPosition;

                if (bytePosition < 0 && HasAudio)
                    bytePosition = Audio.Frames.LastPosition;

                if (bytePosition < 0)
                    bytePosition = ffmpeg.avio_tell(InputContext->pb);

                return bytePosition;
            }
        }

        public double PictureDisplayTimer { get; set; }

        /// <summary>
        /// Maximum duration of a video frame - above this, we consider the jump a timestamp discontinuity
        /// </summary>
        public double MaxPictureDuration { get; private set; }

        public bool IsAtEndOfStream { get; private set; }

        public ClockSync MasterSyncMode
        {
            get
            {
                if (ClockSyncMode == ClockSync.Video)
                {
                    if (HasVideo)
                        return ClockSync.Video;
                    else
                        return ClockSync.Audio;
                }
                else if (ClockSyncMode == ClockSync.Audio)
                {
                    if (HasAudio)
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

        /// <summary>
        /// Gets the current master clock value.
        /// </summary>
        public double MasterTime
        {
            get
            {
                return MasterSyncMode switch
                {
                    ClockSync.Video => VideoClock.Value,
                    ClockSync.Audio => AudioClock.Value,
                    _ => ExternalClock.Value,
                };
            }
        }

        public double ComponentSyncDelay
        {
            get
            {
                var syncDelay = 0d;

                if (HasAudio && HasVideo)
                    syncDelay = AudioClock.Value - VideoClock.Value;
                else if (HasVideo)
                    syncDelay = MasterTime - VideoClock.Value;
                else if (HasAudio)
                    syncDelay = MasterTime - AudioClock.Value;

                return syncDelay;
            }
        }

        public ClockSync ClockSyncMode { get; private set; }

        public Clock AudioClock { get; private set; }

        public Clock VideoClock { get; private set; }

        public Clock ExternalClock { get; private set; }

        public AudioComponent Audio { get; }

        public VideoComponent Video { get; }

        public SubtitleComponent Subtitle { get; }

        public IReadOnlyList<MediaComponent> Components { get; }

        public bool HasVideo => Video != null && Video.Stream != null && Video.StreamIndex >= 0;

        public bool HasAudio => Audio != null && Audio.Stream != null && Audio.StreamIndex >= 0;

        public bool HasSubtitles => Subtitle != null && Subtitle.Stream != null && Subtitle.StreamIndex >= 0;

        private bool HasEnoughPacketCount => Components.All(c => c.HasEnoughPackets);

        private bool HasEnoughPacketSize => Components.Sum(c => c.Packets.Size) > Constants.MAX_QUEUE_SIZE;

        public static MediaContainer Open(ProgramOptions options, IPresenter renderer)
        {
            var container = new MediaContainer(options, renderer);
            renderer.Initialize(container);

            var o = container.Options;
            container.Video.LastStreamIndex = container.Video.StreamIndex = -1;
            container.Audio.LastStreamIndex = container.Audio.StreamIndex = -1;
            container.Subtitle.LastStreamIndex = container.Subtitle.StreamIndex = -1;
            container.FileName = o.InputFileName;
            if (string.IsNullOrWhiteSpace(container.FileName))
                goto fail;

            container.InputFormat = o.InputFormat;
            container.ytop = 0;
            container.xleft = 0;

            container.VideoClock = new Clock(container.Video.Packets);
            container.AudioClock = new Clock(container.Audio.Packets);
            container.ExternalClock = new Clock(container.ExternalClock);

            if (container.Options.StartupVolume < 0)
                Helpers.LogWarning($"-volume={container.Options.StartupVolume} < 0, setting to 0\n");

            if (container.Options.StartupVolume > 100)
                Helpers.LogWarning($"-volume={container.Options.StartupVolume} > 100, setting to 100\n");

            container.Options.StartupVolume = container.Options.StartupVolume.Clamp(0, 100);
            container.Options.StartupVolume = (SDL.SDL_MIX_MAXVOLUME * container.Options.StartupVolume / 100).Clamp(0, SDL.SDL_MIX_MAXVOLUME);
            
            container.IsMuted = false;
            container.ClockSyncMode = container.Options.ClockSyncType;
            container.StartReadThread();
            return container;

        fail:
            container.Close();
            return null;
        }

        /// <summary>
        /// Port of check_external_clock_speed.
        /// </summary>
        public void SyncExternalClockSpeed()
        {
            if (HasVideo && Video.Packets.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES ||
                HasAudio && Audio.Packets.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES)
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

        public void ToggleMute() => IsMuted = !IsMuted;

        public void TogglePause()
        {
            StreamTogglePause();
            IsInStepMode = false;
        }

        public void StreamTogglePause()
        {
            if (IsPaused)
            {
                PictureDisplayTimer += Clock.SystemTime - VideoClock.LastUpdated;
                if (ReadPauseResultCode != ffmpeg.AVERROR(38))
                    VideoClock.IsPaused = false;

                VideoClock.Set(VideoClock.Value, VideoClock.Serial);
            }

            IsPaused = AudioClock.IsPaused = VideoClock.IsPaused = ExternalClock.IsPaused = !IsPaused;
            ExternalClock.Set(ExternalClock.Value, ExternalClock.Serial);
        }

        public void StreamCycleChannel(AVMediaType codecType)
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
                    // check that parameters are OK
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

            Helpers.LogInfo($"Switch {component.MediaTypeString} stream from #{component.StreamIndex} to #{nextStreamIndex}\n");
            component.Close();
            OpenComponent(nextStreamIndex);
        }

        /// <summary>
        /// Port of step_to_next_frame.
        /// </summary>
        public void StepToNextFrame()
        {
            // if the stream is paused unpause it, then step
            if (IsPaused)
                StreamTogglePause();
            
            IsInStepMode = true;
        }

        public void SeekByTimestamp(long targetTimestamp)
            => StreamSeek(targetTimestamp, 0, false);

        public void SeekByTimestamp(long currentTimestamp, long offsetTimestamp)
            => StreamSeek(currentTimestamp, offsetTimestamp, false);

        public void SeekByPosition(long bytePosition)
            => StreamSeek(bytePosition, 0, true);

        public void SeekByPosition(long bytePosition, long byteOffset)
            => StreamSeek(bytePosition, byteOffset, true);

        /// <summary>
        /// Port of seek_chapter.
        /// </summary>
        /// <param name="incrementCount"></param>
        public void ChapterSeek(int incrementCount)
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

            Helpers.LogVerbose($"Seeking to chapter {i}.\n");
            SeekByTimestamp(ffmpeg.av_rescale_q(InputContext->chapters[i]->start, InputContext->chapters[i]->time_base, Constants.AV_TIME_BASE_Q));
        }

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
            var lowResFactor = Options.LowResolution;

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

            var forcedCodecName = targetComponent.WantedCodecName;
            targetComponent.LastStreamIndex = streamIndex;

            if (!string.IsNullOrWhiteSpace(forcedCodecName))
                codec = ffmpeg.avcodec_find_decoder_by_name(forcedCodecName);

            if (codec == null)
            {
                if (!string.IsNullOrWhiteSpace(forcedCodecName))
                    Helpers.LogWarning($"No codec could be found with name '{forcedCodecName}'\n");
                else
                    Helpers.LogWarning($"No decoder could be found for codec {ffmpeg.avcodec_get_name(codecContext->codec_id)}\n");
                ret = ffmpeg.AVERROR(ffmpeg.EINVAL);
                goto fail;
            }

            codecContext->codec_id = codec->id;
            if (lowResFactor > codec->max_lowres)
            {
                Helpers.LogWarning($"The maximum value for lowres supported by the decoder is {codec->max_lowres}\n");
                lowResFactor = codec->max_lowres;
            }

            codecContext->lowres = lowResFactor;

            if (Options.IsFastDecodingEnabled != 0)
                codecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            const string ThreadsOptionKey = "threads";
            const string ThreadsOptionValue = "auto";

            var codecOptions = Helpers.filter_codec_opts(Options.CodecOptions, codecContext->codec_id, ic, ic->streams[streamIndex], codec);
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
                Helpers.LogError($"Option {key} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            IsAtEndOfStream = false;
            ic->streams[streamIndex]->discard = AVDiscard.AVDISCARD_DEFAULT;
            ret = targetComponent.InitializeDecoder(codecContext, streamIndex);
            if (ret < 0) goto fail;
            targetComponent.Start();

            if (targetComponent.IsVideo)
                IsPictureAttachmentPending = true;

            if (targetComponent.IsAudio)
                Renderer.Audio.Pause();

            goto exit;

        fail:
            ffmpeg.avcodec_free_context(&codecContext);
        exit:
            ffmpeg.av_dict_free(&codecOptions);

            return ret;
        }

        /// <summary>
        /// Port of stream_close.
        /// </summary>
        public void Close()
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
        }

        private static bool IsInputFormatRealtime(AVFormatContext* ic)
        {
            var inputFormatName = Helpers.PtrToString(ic->iformat->name);
            if (inputFormatName == "rtp" || inputFormatName == "rtsp" || inputFormatName == "sdp")
                return true;

            var url = Helpers.PtrToString(ic->url)?.ToLowerInvariant();
            url = string.IsNullOrWhiteSpace(url) ? string.Empty : url;

            if (ic->pb != null && (url.StartsWith("rtp:") || url.StartsWith("udp:")))
                return true;

            return false;
        }

        private void StartReadThread()
        {
            ReadingThread = new Thread(ReadingThreadMethod) { IsBackground = true, Name = nameof(ReadingThreadMethod) };
            ReadingThread.Start();
        }

        private int InputInterrupt(void* opaque) => IsAbortRequested ? 1 : 0;

        /// <summary>
        /// Port of stream_seek. Not exposed to improve on code readability.
        /// </summary>
        /// <param name="absoluteTarget">The target byte offset or timestamp.</param>
        /// <param name="relativeTarget">The offset (from target) byte offset or timestamp.</param>
        /// <param name="seekByBytes">Determines if the target is a byte offset or a timestamp.</param>
        private void StreamSeek(long absoluteTarget, long relativeTarget, bool seekByBytes)
        {
            if (IsSeekRequested)
                return;

            SeekAbsoluteTarget = absoluteTarget;
            SeekRelativeTarget = relativeTarget;
            SeekFlags &= ~ffmpeg.AVSEEK_FLAG_BYTE;
            if (seekByBytes)
                SeekFlags |= ffmpeg.AVSEEK_FLAG_BYTE;

            IsSeekRequested = true;
            NeedsMorePacketsEvent.Set();
        }

        private MediaComponent FindComponentByStreamIndex(int streamIndex)
        {
            foreach (var c in Components)
            {
                if (c.StreamIndex == streamIndex)
                    return c;
            }

            return null;
        }

        /// <summary>
        /// This thread gets the stream from the disk or the network
        /// </summary>
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

            if (ffmpeg.av_dict_get(o.FormatOptions, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE) == null)
            {
                var formatOptions = o.FormatOptions;
                ffmpeg.av_dict_set(&formatOptions, "scan_all_pmts", "1", ffmpeg.AV_DICT_DONT_OVERWRITE);
                o.FormatOptions = formatOptions;
                scan_all_pmts_set = true;
            }

            {
                var formatOptions = o.FormatOptions;
                err = ffmpeg.avformat_open_input(&ic, FileName, InputFormat, &formatOptions);
                o.FormatOptions = formatOptions;
            }

            if (err < 0)
            {
                Helpers.LogError($"{FileName}: {Helpers.print_error(err)}\n");
                ret = -1;
                goto fail;
            }

            if (scan_all_pmts_set)
            {
                var formatOptions = o.FormatOptions;
                ffmpeg.av_dict_set(&formatOptions, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE);
                o.FormatOptions = formatOptions;
            }

            AVDictionaryEntry* formatOption;
            if ((formatOption = ffmpeg.av_dict_get(o.FormatOptions, string.Empty, null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                Helpers.LogError($"Option {Helpers.PtrToString(formatOption->key)} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            InputContext = ic;

            if (o.GeneratePts)
                ic->flags |= ffmpeg.AVFMT_FLAG_GENPTS;

            ffmpeg.av_format_inject_global_side_data(ic);

            if (o.IsStreamInfoEnabled)
            {
                var opts = Helpers.setup_find_stream_info_opts(ic, o.CodecOptions);
                int orig_nb_streams = (int)ic->nb_streams;

                err = ffmpeg.avformat_find_stream_info(ic, opts);

                for (i = 0; i < orig_nb_streams; i++)
                    ffmpeg.av_dict_free(&opts[i]);

                ffmpeg.av_freep(&opts);

                if (err < 0)
                {
                    Helpers.LogWarning($"{FileName}: could not find codec parameters\n");
                    ret = -1;
                    goto fail;
                }
            }

            if (ic->pb != null)
                ic->pb->eof_reached = 0; // FIXME hack, ffplay maybe should not use avio_feof() to test for the end

            if (o.IsByteSeekingEnabled.IsAuto())
                o.IsByteSeekingEnabled = ic->iformat->flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) && Helpers.PtrToString(ic->iformat->name) != "ogg" ? ThreeState.On : ThreeState.Off;

            MaxPictureDuration = ic->iformat->flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) ? 10.0 : 3600.0;

            if (string.IsNullOrWhiteSpace(Renderer.Video.WindowTitle) && (formatOption = ffmpeg.av_dict_get(ic->metadata, "title", null, 0)) != null)
                Renderer.Video.WindowTitle = $"{Helpers.PtrToString(formatOption->value)} - {o.InputFileName}";

            /* if seeking requested, we execute it */
            if (o.StartOffset.IsValidPts())
            {
                var startTimestamp = o.StartOffset;
                /* add the stream start time */
                if (ic->start_time.IsValidPts())
                    startTimestamp += ic->start_time;

                ret = ffmpeg.avformat_seek_file(ic, -1, long.MinValue, startTimestamp, long.MaxValue, 0);
                if (ret < 0)
                    Helpers.LogWarning($"{FileName}: could not seek to position {(startTimestamp / Clock.TimeBaseMicros)}\n");
            }

            IsRealtime = IsInputFormatRealtime(ic);

            if (o.ShowStatus != 0)
                ffmpeg.av_dump_format(ic, 0, FileName, 0);

            for (i = 0; i < ic->nb_streams; i++)
            {
                var st = ic->streams[i];
                var type = st->codecpar->codec_type;
                st->discard = AVDiscard.AVDISCARD_ALL;
                if (type >= 0 && o.WantedStreams[type] != null && streamIndexes[type] == -1)
                    if (ffmpeg.avformat_match_stream_specifier(ic, st, o.WantedStreams[type]) > 0)
                        streamIndexes[type] = i;
            }

            foreach (var kvp in o.WantedStreams)
            {
                if (kvp.Value != null && streamIndexes[(AVMediaType)i] == -1)
                {
                    Helpers.LogError($"Stream specifier {kvp.Value} does not match any {ffmpeg.av_get_media_type_string(kvp.Key)} stream\n");
                    streamIndexes[kvp.Key] = int.MaxValue;
                }
            }

            if (!o.IsVideoDisabled)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_VIDEO,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO], -1, null, 0);
            if (!o.IsAudioDisabled)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_AUDIO,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO],
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO],
                                        null, 0);
            if (!o.IsVideoDisabled && !o.IsSubtitleDisabled)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_SUBTITLE,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE],
                                        (streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0 ?
                                         streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] :
                                         streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO]),
                                        null, 0);

            ShowMode = o.ShowMode;
            if (streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                AVStream* st = ic->streams[streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO]];
                AVCodecParameters* codecpar = st->codecpar;
                AVRational sar = ffmpeg.av_guess_sample_aspect_ratio(ic, st, null);
                if (codecpar->width != 0)
                    Renderer.Video.set_default_window_size(codecpar->width, codecpar->height, sar);
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

            if (ShowMode == ShowMode.None)
                ShowMode = ret >= 0 ? ShowMode.Video : ShowMode.None;

            if (streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE] >= 0)
            {
                OpenComponent(streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE]);
            }

            if (Video.StreamIndex < 0 && Audio.StreamIndex < 0)
            {
                Helpers.LogFatal($"Failed to open file '{FileName}' or configure filtergraph\n");
                ret = -1;
                goto fail;
            }

            if (o.IsInfiniteBufferEnabled < 0 && IsRealtime)
                o.IsInfiniteBufferEnabled = ThreeState.On;

            while (true)
            {
                if (IsAbortRequested)
                    break;

                if (IsPaused != WasPaused)
                {
                    WasPaused = IsPaused;
                    if (IsPaused)
                        ReadPauseResultCode = ffmpeg.av_read_pause(ic);
                    else
                        ffmpeg.av_read_play(ic);
                }

                if (IsPaused &&
                        (Helpers.PtrToString(ic->iformat->name) == "rtsp" ||
                         (ic->pb != null && o.InputFileName.StartsWith("mmsh:"))))
                {
                    /* wait 10 ms to avoid trying to get another packet */
                    /* XXX: horrible */
                    SDL.SDL_Delay(10);
                    continue;
                }

                if (IsSeekRequested)
                {
                    var seekTarget = SeekAbsoluteTarget;
                    var seekTargetMin = SeekRelativeTarget > 0 ? seekTarget - SeekRelativeTarget + 2 : long.MinValue;
                    var seekTargetMax = SeekRelativeTarget < 0 ? seekTarget - SeekRelativeTarget - 2 : long.MaxValue;
                    // FIXME the +-2 is due to rounding being not done in the correct direction in generation
                    //      of the seek_pos/seek_rel variables

                    ret = ffmpeg.avformat_seek_file(InputContext, -1, seekTargetMin, seekTarget, seekTargetMax, SeekFlags);
                    if (ret < 0)
                    {
                        Helpers.LogError($"{Helpers.PtrToString(InputContext->url)}: error while seeking\n");
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
                            : seekTarget / Clock.TimeBaseMicros, 0);
                    }

                    IsSeekRequested = false;
                    IsPictureAttachmentPending = true;
                    IsAtEndOfStream = false;

                    if (IsPaused)
                        StepToNextFrame();
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
                if (o.IsInfiniteBufferEnabled != ThreeState.On && (HasEnoughPacketSize || HasEnoughPacketCount))
                {
                    /* wait 10 ms */
                    NeedsMorePacketsEvent.WaitOne(10);
                    continue;
                }

                if (!IsPaused && Audio.HasFinishedDecoding && Video.HasFinishedDecoding)
                {
                    if (o.LoopCount != 1 && (o.LoopCount == 0 || (--o.LoopCount) > 0))
                    {
                        SeekByTimestamp(o.StartOffset.IsValidPts() ? o.StartOffset : 0);
                    }
                    else if (o.ExitOnFinish)
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
                        if (o.ExitOnFinish)
                            goto fail;
                        else
                            break;
                    }

                    NeedsMorePacketsEvent.WaitOne(10);
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

                var isPacketInPlayRange = !o.Duration.IsValidPts() ||
                        (packetPts - (streamStartPts.IsValidPts() ? streamStartPts : 0)) *
                        ic->streams[readPacket->stream_index]->time_base.ToFactor() -
                        (o.StartOffset.IsValidPts() ? o.StartOffset : 0) / Clock.TimeBaseMicros
                        <= (o.Duration / Clock.TimeBaseMicros);

                var component = FindComponentByStreamIndex(readPacket->stream_index);
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
                SDL.SDL_Event sdlEvent = new();
                sdlEvent.type = (SDL.SDL_EventType)Constants.FF_QUIT_EVENT;
                _ = SDL.SDL_PushEvent(ref sdlEvent);
            }

            return; // 0;
        }
    }
}
