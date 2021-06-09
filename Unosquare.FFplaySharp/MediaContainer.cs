namespace Unosquare.FFplaySharp
{
    using FFmpeg;
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

        private FFInputFormat InputFormat = null;
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

        public FFFormatContext InputContext { get; private set; }

        public IPresenter Renderer { get; }

        public ProgramOptions Options { get; }

        public AutoResetEvent NeedsMorePacketsEvent { get; } = new(true);

        public string FileName { get; private set; }

        public bool IsAbortRequested { get; private set; }

        public bool IsSeekRequested { get; private set; }

        public bool IsSeekMethodUnknown =>
            InputContext != null &&
            InputContext.IsNull == false &&
            InputContext.Pointer->iformat != null &&
            InputContext.Pointer->iformat->flags.HasFlag(Constants.SeekMethodUnknownFlags) &&
            InputContext.Pointer->iformat->read_seek.Pointer.IsNull();

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
                    bytePosition = Video.Frames.ShownBytePosition;

                if (bytePosition < 0 && HasAudio)
                    bytePosition = Audio.Frames.ShownBytePosition;

                if (bytePosition < 0)
                    bytePosition = ffmpeg.avio_tell(InputContext.Pointer->pb);

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

        private bool HasEnoughPacketSize => Components.Sum(c => c.Packets.ByteSize) > Constants.MAX_QUEUE_SIZE;

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

            container.InputFormat = o.InputFormat ?? FFInputFormat.None;
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

                VideoClock.Set(VideoClock.Value, VideoClock.GroupIndex);
            }

            IsPaused = AudioClock.IsPaused = VideoClock.IsPaused = ExternalClock.IsPaused = !IsPaused;
            ExternalClock.Set(ExternalClock.Value, ExternalClock.GroupIndex);
        }

        public void StreamCycleChannel(AVMediaType codecType)
        {
            var streamCount = InputContext.Streams.Count;

            MediaComponent component = codecType switch
            {
                AVMediaType.AVMEDIA_TYPE_VIDEO => Video,
                AVMediaType.AVMEDIA_TYPE_AUDIO => Audio,
                _ => Subtitle
            };

            var startStreamIndex = component.LastStreamIndex;
            var nextStreamIndex = startStreamIndex;

            FFProgram program = null;
            if (component.IsVideo && component.StreamIndex != -1)
            {
                program = InputContext.FindProgramByStream(component.StreamIndex);
                if (program != null)
                {
                    var streamIndices = program.StreamIndices;

                    for (startStreamIndex = 0; startStreamIndex < streamIndices.Count; startStreamIndex++)
                    {
                        if (streamIndices[startStreamIndex] == nextStreamIndex)
                            break;
                    }

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

                var resultStreamIndex = program != null && !program.IsNull
                    ? Convert.ToInt32(program.StreamIndices[nextStreamIndex])
                    : nextStreamIndex;

                var st = InputContext.Streams[resultStreamIndex];

                if (st.CodecParameters.CodecType == component.MediaType)
                {
                    // check that parameters are OK
                    switch (component.MediaType)
                    {
                        case AVMediaType.AVMEDIA_TYPE_AUDIO:
                            if (st.CodecParameters.SampleRate != 0 &&
                                st.CodecParameters.Channels != 0)
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
                nextStreamIndex = program.StreamIndices[nextStreamIndex];

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

            if (InputContext.ChapterCount <= 0)
                return;

            /* find the current chapter */
            for (i = 0; i < InputContext.ChapterCount; i++)
            {
                var chapter = InputContext.Pointer->chapters[i];
                if (ffmpeg.av_compare_ts(pos, Constants.AV_TIME_BASE_Q, chapter->start, chapter->time_base) < 0)
                {
                    i--;
                    break;
                }
            }

            i += incrementCount;
            i = Math.Max(i, 0);
            if (i >= InputContext.ChapterCount)
                return;

            Helpers.LogVerbose($"Seeking to chapter {i}.\n");
            SeekByTimestamp(ffmpeg.av_rescale_q(InputContext.Pointer->chapters[i]->start, InputContext.Pointer->chapters[i]->time_base, Constants.AV_TIME_BASE_Q));
        }

        /// <summary>
        /// Open a given stream index. Returns 0 if OK.
        /// Port of stream_component_open
        /// </summary>
        /// <param name="streamIndex"></param>
        /// <returns>0 if OK</returns>
        public int OpenComponent(int streamIndex)
        {
            var ret = 0;
            var lowResFactor = Options.LowResolution;

            if (streamIndex < 0 || streamIndex >= InputContext.Streams.Count)
                return -1;

            var codecContext = new FFCodecContext();
            ret = codecContext.ApplyStreamParameters(InputContext.Streams[streamIndex]);

            if (ret < 0) goto fail;
            codecContext.PacketTimeBase = InputContext.Streams[streamIndex].TimeBase;

            var codec = FFCodec.FromDecoderId(codecContext.CodecId);
            var targetMediaType = codecContext.CodecType;

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
                codec = FFCodec.FromDecoderName(forcedCodecName);

            if (codec == null)
            {
                if (!string.IsNullOrWhiteSpace(forcedCodecName))
                    Helpers.LogWarning($"No codec could be found with name '{forcedCodecName}'\n");
                else
                    Helpers.LogWarning($"No decoder could be found for codec {codecContext.CodecName}\n");
                ret = ffmpeg.AVERROR(ffmpeg.EINVAL);
                goto fail;
            }

            codecContext.CodecId = codec.Id;
            if (lowResFactor > codec.MaxLowResFactor)
            {
                Helpers.LogWarning($"The maximum value for lowres supported by the decoder is {codec.MaxLowResFactor}\n");
                lowResFactor = codec.MaxLowResFactor;
            }

            codecContext.LowResFactor = lowResFactor;

            if (Options.IsFastDecodingEnabled != 0)
                codecContext.Flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            const string ThreadsOptionKey = "threads";
            const string ThreadsOptionValue = "auto";

            var codecOptions = Helpers.FilterCodecOptions(
                Options.CodecOptions,
                codecContext.CodecId,
                InputContext,
                InputContext.Streams[streamIndex],
                codec);

            if (!codecOptions.ContainsKey(ThreadsOptionKey))
                codecOptions[ThreadsOptionKey] = ThreadsOptionValue;

            if (lowResFactor != 0)
                codecOptions["lowres"] = $"{lowResFactor}";

            if (targetMediaType == AVMediaType.AVMEDIA_TYPE_VIDEO || targetMediaType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                codecOptions["refcounted_frames"] = "1";

            ret = codecContext.Open(codec, codecOptions);

            var invalidKey = codecOptions.First?.Key;
            codecOptions.Release();

            if (ret < 0)
                goto fail;

            if (invalidKey != null)
            {
                Helpers.LogError($"Option {invalidKey} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            IsAtEndOfStream = false;
            InputContext.Streams[streamIndex].DiscardFlags = AVDiscard.AVDISCARD_DEFAULT;
            ret = targetComponent.InitializeDecoder(codecContext, streamIndex);
            if (ret < 0) goto fail;
            targetComponent.Start();

            if (targetComponent.IsVideo)
                IsPictureAttachmentPending = true;

            if (targetComponent.IsAudio)
                Renderer.Audio.Pause();

            goto exit;

        fail:
            codecContext.Release();
        exit:
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
            InputContext.Release();
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

        private static bool IsInputFormatRealtime(FFFormatContext ic)
        {
            var inputFormatName = Helpers.PtrToString(ic.Pointer->iformat->name);
            if (inputFormatName == "rtp" || inputFormatName == "rtsp" || inputFormatName == "sdp")
                return true;

            var url = Helpers.PtrToString(ic.Pointer->url)?.ToLowerInvariant();
            url = string.IsNullOrWhiteSpace(url) ? string.Empty : url;

            if (ic.Pointer->pb != null && (url.StartsWith("rtp:") || url.StartsWith("udp:")))
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

            var ic = new FFFormatContext
            {
                InterruptCallback = InputInterruptCallback
            };

            var formatOptions = o.FormatOptions.ToUnmanaged();
            if (!formatOptions.ContainsKey("scan_all_pmts"))
            {
                formatOptions["scan_all_pmts"] = "1";
                scan_all_pmts_set = true;
            }

            err = ic.OpenInput(FileName, InputFormat, formatOptions);

            if (err < 0)
            {
                Helpers.LogError($"{FileName}: {Helpers.print_error(err)}\n");
                ret = -1;
                goto fail;
            }

            if (scan_all_pmts_set)
                formatOptions.Remove("scan_all_pmts");

            var invalidOptionKey = formatOptions.First?.Key;
            formatOptions.Release();

            if (invalidOptionKey != null)
            {
                Helpers.LogError($"Option {invalidOptionKey} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            InputContext = ic;

            if (o.GeneratePts)
                ic.Flags |= ffmpeg.AVFMT_FLAG_GENPTS;

            ic.InjectGlobalSideData();

            if (o.IsStreamInfoEnabled)
            {
                var perStreamOptionsList = Helpers.FindStreamInfoOptions(ic, o.CodecOptions);
                var perStreamOptions = (AVDictionary**)ffmpeg.av_mallocz_array((ulong)perStreamOptionsList.Count, (ulong)sizeof(IntPtr));
                for (var optionIndex = 0; optionIndex < perStreamOptionsList.Count; optionIndex++)
                    perStreamOptions[optionIndex] = perStreamOptionsList[optionIndex].Pointer;

                err = ffmpeg.avformat_find_stream_info(ic.Pointer, perStreamOptions);
                ffmpeg.av_freep(&perStreamOptions);

                foreach (var optionsDictionary in perStreamOptionsList)
                    optionsDictionary.Release();

                if (err < 0)
                {
                    Helpers.LogWarning($"{FileName}: could not find codec parameters\n");
                    ret = -1;
                    goto fail;
                }
            }

            if (ic.Pointer->pb != null)
                ic.Pointer->pb->eof_reached = 0; // FIXME hack, ffplay maybe should not use avio_feof() to test for the end

            if (o.IsByteSeekingEnabled.IsAuto())
                o.IsByteSeekingEnabled = ic.Pointer->iformat->flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) && Helpers.PtrToString(ic.Pointer->iformat->name) != "ogg" ? ThreeState.On : ThreeState.Off;

            MaxPictureDuration = ic.Pointer->iformat->flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) ? 10.0 : 3600.0;

            var metadata = FFDictionary.Extract(ic.Pointer->metadata);
            if (string.IsNullOrWhiteSpace(Renderer.Video.WindowTitle) && metadata.ContainsKey("title"))
                Renderer.Video.WindowTitle = $"{metadata["title"]} - {o.InputFileName}";

            /* if seeking requested, we execute it */
            if (o.StartOffset.IsValidPts())
            {
                var startTimestamp = o.StartOffset;
                /* add the stream start time */
                if (ic.Pointer->start_time.IsValidPts())
                    startTimestamp += ic.Pointer->start_time;

                ret = ffmpeg.avformat_seek_file(ic.Pointer, -1, long.MinValue, startTimestamp, long.MaxValue, 0);
                if (ret < 0)
                    Helpers.LogWarning($"{FileName}: could not seek to position {(startTimestamp / Clock.TimeBaseMicros)}\n");
            }

            IsRealtime = IsInputFormatRealtime(ic);

            if (o.ShowStatus != 0)
                ffmpeg.av_dump_format(ic.Pointer, 0, FileName, 0);

            for (i = 0; i < ic.Streams.Count; i++)
            {
                var st = ic.Streams[i];
                var type = st.CodecParameters.CodecType;
                st.DiscardFlags = AVDiscard.AVDISCARD_ALL;
                if (type >= 0 && o.WantedStreams.ContainsKey(type) && o.WantedStreams[type] != null && streamIndexes[type] == -1)
                    if (ffmpeg.avformat_match_stream_specifier(ic.Pointer, st.Pointer, o.WantedStreams[type]) > 0)
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
                    ffmpeg.av_find_best_stream(ic.Pointer, AVMediaType.AVMEDIA_TYPE_VIDEO,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO], -1, null, 0);
            if (!o.IsAudioDisabled)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] =
                    ffmpeg.av_find_best_stream(ic.Pointer, AVMediaType.AVMEDIA_TYPE_AUDIO,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO],
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO],
                                        null, 0);
            if (!o.IsVideoDisabled && !o.IsSubtitleDisabled)
                streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE] =
                    ffmpeg.av_find_best_stream(ic.Pointer, AVMediaType.AVMEDIA_TYPE_SUBTITLE,
                                        streamIndexes[AVMediaType.AVMEDIA_TYPE_SUBTITLE],
                                        (streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0 ?
                                         streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] :
                                         streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO]),
                                        null, 0);

            ShowMode = o.ShowMode;
            if (streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                var st = ic.Streams[streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO]];
                var codecpar = st.CodecParameters;
                var sar = ic.GuessAspectRatio(st, null);
                if (codecpar.Width != 0)
                    Renderer.Video.set_default_window_size(codecpar.Width, codecpar.Height, sar);
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
                        ReadPauseResultCode = ffmpeg.av_read_pause(ic.Pointer);
                    else
                        ffmpeg.av_read_play(ic.Pointer);
                }

                if (IsPaused &&
                        (Helpers.PtrToString(ic.Pointer->iformat->name) == "rtsp" ||
                         (ic.Pointer->pb != null && o.InputFileName.StartsWith("mmsh:"))))
                {
                    /* wait 10 ms to avoid trying to get another packet */
                    /* XXX: horrible */
                    SDL.SDL_Delay(10);
                    continue;
                }

                if (IsSeekRequested)
                    InputContextHandleSeek(ref ret);

                if (IsPictureAttachmentPending)
                {
                    if (Video.IsPictureAttachmentStream)
                    {
                        var copy = FFPacket.Clone(&Video.Stream.Pointer->attached_pic);
                        Video.Packets.Enqueue(copy);
                        Video.Packets.EnqueueNull();
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
                
                var flowResult = ReadPacket(ref ret);
                if (flowResult == FlowResult.Fail)
                    goto fail;
                else if (flowResult == FlowResult.LoopBreak)
                    break;
                else if (flowResult == FlowResult.LoopContinue)
                    continue;
            }

            ret = 0;
        fail:
            if (ic != null && InputContext == null)
                ic.Release();

            if (ret != 0)
            {
                SDL.SDL_Event sdlEvent = new();
                sdlEvent.type = (SDL.SDL_EventType)Constants.FF_QUIT_EVENT;
                _ = SDL.SDL_PushEvent(ref sdlEvent);
            }

            return; // 0;
        }

        private void InputContextHandleSeek(ref int resultCode)
        {
            var seekTarget = SeekAbsoluteTarget;
            var seekTargetMin = SeekRelativeTarget > 0 ? seekTarget - SeekRelativeTarget + 2 : long.MinValue;
            var seekTargetMax = SeekRelativeTarget < 0 ? seekTarget - SeekRelativeTarget - 2 : long.MaxValue;
            // FIXME the +-2 is due to rounding being not done in the correct direction in generation
            //      of the seek_pos/seek_rel variables

            resultCode = ffmpeg.avformat_seek_file(InputContext.Pointer, -1, seekTargetMin, seekTarget, seekTargetMax, SeekFlags);
            if (resultCode < 0)
            {
                Helpers.LogError($"{Helpers.PtrToString(InputContext.Pointer->url)}: error while seeking\n");
            }
            else
            {
                foreach (var c in Components)
                {
                    if (c.StreamIndex < 0)
                        continue;

                    c.Packets.Clear();
                    c.Packets.EnqueueFlush();
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

        private FlowResult ReadPacket(ref int resultCode)
        {
            var readPacket = new FFPacket();
            resultCode = ffmpeg.av_read_frame(InputContext.Pointer, readPacket.Pointer);

            if (resultCode < 0)
            {
                readPacket.Release();

                if ((resultCode == ffmpeg.AVERROR_EOF || ffmpeg.avio_feof(InputContext.Pointer->pb) != 0) && !IsAtEndOfStream)
                {
                    foreach (var c in Components)
                    {
                        if (c.StreamIndex >= 0)
                            c.Packets.EnqueueNull();
                    }

                    IsAtEndOfStream = true;
                }

                if (InputContext.Pointer->pb != null && InputContext.Pointer->pb->error != 0)
                {
                    if (Options.ExitOnFinish)
                        return FlowResult.Fail;
                    else
                        return FlowResult.LoopBreak;
                }

                NeedsMorePacketsEvent.WaitOne(10);
                return FlowResult.LoopContinue;
            }
            else
            {
                IsAtEndOfStream = false;
            }

            // check if packet is in play range specified by user, then queue, otherwise discard.
            var streamStartPts = InputContext.Streams[readPacket.StreamIndex].StartTime;

            var isPacketInPlayRange = !Options.Duration.IsValidPts() ||
                    (readPacket.Pts - (streamStartPts.IsValidPts() ? streamStartPts : 0)) *
                    InputContext.Pointer->streams[readPacket.StreamIndex]->time_base.ToFactor() -
                    (Options.StartOffset.IsValidPts() ? Options.StartOffset : 0) / Clock.TimeBaseMicros
                    <= (Options.Duration / Clock.TimeBaseMicros);

            var component = FindComponentByStreamIndex(readPacket.StreamIndex);
            if (component != null && !component.IsPictureAttachmentStream && isPacketInPlayRange)
                component.Packets.Enqueue(readPacket);
            else
                readPacket.Release();

            return FlowResult.Next;
        }
    }
}
