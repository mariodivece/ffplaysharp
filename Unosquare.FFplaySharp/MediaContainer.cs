﻿namespace Unosquare.FFplaySharp;

public unsafe class MediaContainer
{
    private readonly AVIOInterruptCB_callback InputInterruptCallback;

    private FFInputFormat InputFormat;
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

    private MediaContainer(ProgramOptions options, IPresenter presenter)
    {
        InputInterruptCallback = new(InputInterrupt);
        Options = options ?? new();
        Audio = new(this);
        Video = new(this);
        Subtitle = new(this);
        Presenter = presenter;
        Components = [Audio, Video, Subtitle];
    }

    public FFFormatContext Input { get; private set; }

    public IPresenter Presenter { get; }

    public ProgramOptions Options { get; }

    internal EventAwaiter NeedsMorePacketsEvent { get; } = new();

    public string FileName { get; private set; }

    public bool IsAbortRequested { get; private set; }

    public bool IsSeekRequested { get; private set; }

    public long SeekAbsoluteTarget { get; private set; }

    public bool IsInStepMode { get; private set; }

    public bool IsPaused { get; private set; }

    public bool IsMuted { get; private set; }

    public bool IsRealTime { get; private set; }

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
                bytePosition = Input.IO.BytePosition;

            return bytePosition;
        }
    }

    public TimeExtent PictureDisplayTimer { get; set; }

    /// <summary>
    /// Maximum duration of a video frame - above this, we consider the jump a timestamp discontinuity
    /// </summary>
    public TimeExtent MaxPictureDuration { get; private set; }

    public bool IsAtEndOfStream { get; private set; }

    public ClockSource MasterSyncMode
    {
        get
        {
            if (ClockSyncMode == ClockSource.Video)
            {
                if (HasVideo)
                    return ClockSource.Video;
                else
                    return ClockSource.Audio;
            }
            else if (ClockSyncMode == ClockSource.Audio)
            {
                if (HasAudio)
                    return ClockSource.Audio;
                else
                    return ClockSource.External;
            }
            else
            {
                return ClockSource.External;
            }
        }
    }

    /// <summary>
    /// Gets the current master clock value.
    /// </summary>
    public TimeExtent MasterTime
    {
        get
        {
            return MasterSyncMode switch
            {
                ClockSource.Video => VideoClock.Value,
                ClockSource.Audio => AudioClock.Value,
                _ => ExternalClock.Value,
            };
        }
    }

    public TimeExtent ComponentSyncDelay
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

    public string? Title { get; private set; }

    public ClockSource ClockSyncMode { get; private set; }

    public Clock AudioClock { get; private set; }

    public Clock VideoClock { get; private set; }

    public Clock ExternalClock { get; private set; }

    public AudioComponent Audio { get; }

    public VideoComponent Video { get; }

    public SubtitleComponent Subtitle { get; }

    public IReadOnlyList<MediaComponent> Components { get; }

    public bool HasVideo => Video is not null && Video.Stream.IsValid() && Video.StreamIndex >= 0;

    public bool HasAudio => Audio is not null && Audio.Stream.IsValid() && Audio.StreamIndex >= 0;

    public bool HasSubtitles => Subtitle is not null && Subtitle.Stream.IsValid() && Subtitle.StreamIndex >= 0;

    private bool HasEnoughPacketCount => Components.All(c => c.HasEnoughPackets);

    private bool HasEnoughPacketBuffer => Components.Sum(c => c.Packets.ByteSize) > Constants.MaxQueueSize;

    public static MediaContainer? Open(ProgramOptions options, IPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);

        var container = new MediaContainer(options, presenter);

        try
        {
            presenter.Initialize(container);
            var o = container.Options;

            if (string.IsNullOrWhiteSpace(o.InputFileName))
                throw new ArgumentException($"{nameof(options)}.{nameof(options.InputFileName)} cannot be null.");

            container.FileName = o.InputFileName;
            container.InputFormat = o.InputFormat ?? new();
            container.ytop = 0;
            container.xleft = 0;

            container.VideoClock = new Clock(container.Video.Packets);
            container.AudioClock = new Clock(container.Audio.Packets);
            container.ExternalClock = new Clock(container.ExternalClock);

            container.IsMuted = false;
            container.ClockSyncMode = container.Options.ClockSyncType;
            container.StartReadThread();
            return container;
        }
        catch
        {
            container.Close();
            return null;
            throw;
        }
    }


    /// <summary>
    /// Port of update_video_pts
    /// </summary>
    public void UpdateVideoPts(double pts, int groupIndex)
    {
        /* update current video pts */
        VideoClock.Set(pts, groupIndex);
        ExternalClock.SyncToSlave(VideoClock);
    }

    /// <summary>
    /// Port of check_external_clock_speed.
    /// </summary>
    public void SyncExternalClockSpeed()
    {
        if (HasVideo && Video.Packets.Count <= Constants.ExternalClockMinFrames ||
            HasAudio && Audio.Packets.Count <= Constants.ExternalClockMinFrames)
        {
            ExternalClock.SetSpeed(Math.Max(Constants.ExternalClockSpeedMin, ExternalClock.SpeedRatio - Constants.ExternalClockSpeedStep));
        }
        else if ((Video.StreamIndex < 0 || Video.Packets.Count > Constants.ExternalClockMaxFrames) &&
                 (Audio.StreamIndex < 0 || Audio.Packets.Count > Constants.ExternalClockMaxFrames))
        {
            ExternalClock.SetSpeed(Math.Min(Constants.ExternalClockSpeedMax, ExternalClock.SpeedRatio + Constants.ExternalClockSpeedStep));
        }
        else
        {
            var speed = ExternalClock.SpeedRatio;
            if (speed != 1.0)
                ExternalClock.SetSpeed(speed + Constants.ExternalClockSpeedStep * (1.0 - speed) / Math.Abs(1.0 - speed));
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
        var streamCount = Input.Streams.Count;

        MediaComponent component = codecType switch
        {
            AVMediaType.AVMEDIA_TYPE_VIDEO => Video,
            AVMediaType.AVMEDIA_TYPE_AUDIO => Audio,
            _ => Subtitle
        };

        var startStreamIndex = component.LastStreamIndex;
        var nextStreamIndex = startStreamIndex;

        FFProgram? program = default;
        if (component.MediaType.IsVideo() && component.StreamIndex != -1 && component.Stream.IsValid())
        {
            var programs = component.Stream.FindPrograms();
            program = programs.Any() ? programs[0] : default;
            if (program.IsValid())
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
                if (component.MediaType.IsSubtitle())
                {
                    nextStreamIndex = -1;
                    Subtitle.LastStreamIndex = -1;
                    break;
                }
                if (startStreamIndex == -1)
                    return;

                nextStreamIndex = 0;
            }

            if (nextStreamIndex == startStreamIndex)
                return;

            var resultStreamIndex = program.IsValid()
                ? Convert.ToInt32(program!.StreamIndices[nextStreamIndex])
                : nextStreamIndex;

            var st = Input.Streams[resultStreamIndex];

            if (st.CodecParameters.CodecType == component.MediaType)
            {
                // Check media types and parameters are ok.
                if (component.MediaType is AVMediaType.AVMEDIA_TYPE_AUDIO &&
                    st.CodecParameters.SampleRate != 0 &&
                    st.CodecParameters.Channels != 0)
                    break;

                if (component.MediaType is AVMediaType.AVMEDIA_TYPE_VIDEO or
                    AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    break;
            }
        }

        // Finally, close the existing component and open the new one.
        if (program.IsValid() && nextStreamIndex != -1)
            nextStreamIndex = program!.StreamIndices[nextStreamIndex];

        ($"Switch {component.MediaTypeString} stream from #{component.StreamIndex} to #{nextStreamIndex}.").LogInfo();
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
        if (Input.Chapters.Count <= 0)
            return;

        int i;
        var pos = (long)(MasterTime * Clock.TimeBaseMicros);

        // find the current chapter
        for (i = 0; i < Input.Chapters.Count; i++)
        {
            var chapter = Input.Chapters[i];
            if (ffmpeg.av_compare_ts(pos, Constants.AV_TIME_BASE_Q, chapter.StartTime, chapter.TimeBase) < 0)
            {
                i--;
                break;
            }
        }

        i += incrementCount;
        i = Math.Max(i, 0);
        if (i >= Input.Chapters.Count)
            return;

        ($"Seeking to chapter {i}.").LogVerbose();
        SeekByTimestamp(ffmpeg.av_rescale_q(Input.Chapters[i].StartTime, Input.Chapters[i].TimeBase, Constants.AV_TIME_BASE_Q));
    }

    /// <summary>
    /// Open a given stream index. Returns 0 if OK.
    /// Port of stream_component_open
    /// </summary>
    /// <param name="streamIndex"></param>
    /// <returns>0 if OK</returns>
    public void OpenComponent(int streamIndex)
    {
        if (streamIndex < 0 || streamIndex >= Input.Streams.Count)
            return;

        var codecContext = new FFCodecContext();

        try
        {
            codecContext.ApplyStreamParameters(Input.Streams[streamIndex]);
            codecContext.PacketTimeBase = Input.Streams[streamIndex].TimeBase;

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
            codec = !string.IsNullOrWhiteSpace(forcedCodecName)
                ? FFCodec.FromDecoderName(forcedCodecName)
                : codec;

            if (codec.IsVoid())
            {
                var codecName = !string.IsNullOrWhiteSpace(forcedCodecName)
                    ? forcedCodecName
                    : codecContext.CodecName;

                ($"No decoder could be found for codec {codecName}.").LogWarning();
                throw new FFmpegException(ffmpeg.AVERROR(ffmpeg.EINVAL), $"Could not find codec with name '{codecName}'");
            }

            codecContext.CodecId = codec.Id;

            var lowResFactor = Options.LowResolution;
            if (lowResFactor > codec.MaxLowResFactor)
            {
                ($"The maximum value for lowres supported by the decoder is {codec.MaxLowResFactor}.").LogWarning();
                lowResFactor = codec.MaxLowResFactor;
            }

            codecContext.LowResFactor = lowResFactor;

            if (Options.IsFastDecodingEnabled == ThreeState.On)
                codecContext.Flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            var codecOptions = Input.FilterCodecOptions(
                Options.CodecOptions,
                codecContext.CodecId,
                Input.Streams[streamIndex],
                codec);

            const string ThreadsOptionKey = "threads";
            const string ThreadsOptionValue = "auto";

            if (!codecOptions.ContainsKey(ThreadsOptionKey))
                codecOptions[ThreadsOptionKey] = ThreadsOptionValue;

            if (lowResFactor != 0)
                codecOptions["lowres"] = $"{lowResFactor}";

            codecOptions.Set("flags", "+copy_opaque", ffmpeg.AV_DICT_MULTIKEY);

            try
            {
                codecContext.Open(codec, codecOptions);
                var invalidKey = codecOptions.First?.Key;
                if (invalidKey is not null)
                {
                    ($"Option {invalidKey} not found.").LogError();
                    throw new FFmpegException(ffmpeg.AVERROR_OPTION_NOT_FOUND, $"Option {invalidKey} not found.");
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                codecOptions.Dispose();
            }

            IsAtEndOfStream = false;
            Input.Streams[streamIndex].DiscardFlags = AVDiscard.AVDISCARD_DEFAULT;
            targetComponent.InitializeDecoder(codecContext, streamIndex);

            targetComponent.Start();

            if (targetComponent.MediaType.IsVideo())
                IsPictureAttachmentPending = true;

            if (targetComponent.MediaType.IsAudio())
                Presenter.PauseAudioDevice();
        }
        catch
        {
            codecContext.Dispose();
            throw;
        }
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
        Input?.Dispose();
        Input = default;

        // Release packets and frames
        foreach (var component in Components)
        {
            component.Packets?.Dispose();
            component.Frames?.Dispose();
        }

        NeedsMorePacketsEvent.SignalAll();
        Video.ConvertContext?.Dispose();
        Subtitle.ConvertContext?.Dispose();
    }

    private void StartReadThread()
    {
        ReadingThread = new Thread(ReadingThreadMethod)
        {
            IsBackground = true,
            Name = nameof(ReadingThreadMethod),
            Priority = Constants.ReadingPriority
        };

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
        NeedsMorePacketsEvent.Signal();
    }

    private MediaComponent? FindComponentByStreamIndex(int streamIndex)
    {
        /*
        foreach (var c in Components)
        {
            if (c.StreamIndex == streamIndex)
                return c;
        }
        */

        for (var i = 0; i < Components.Count; i++)
        {
            if (Components[i] is MediaComponent c && c.StreamIndex == streamIndex)
                return c;
        }

        return default;
    }

    private void PrepareInput()
    {
        IsAtEndOfStream = false;

        Input = new FFFormatContext
        {
            InterruptCallback = InputInterruptCallback
        };

        var formatOptions = Options.FormatOptions.ToUnmanaged();
        try
        {
            Input.OpenInput(FileName, ref InputFormat, formatOptions);
            var invalidOptionKey = formatOptions.First?.Key;
            if (invalidOptionKey is not null)
            {
                ($"Option {invalidOptionKey} not found.").LogError();
                throw new FFmpegException(ffmpeg.AVERROR_OPTION_NOT_FOUND, $"Option {invalidOptionKey} not found.");
            }


            if (Options.GeneratePts)
                Input.Flags |= ffmpeg.AVFMT_FLAG_GENPTS;

            Input.InjectGlobalSideData();

            if (Options.IsStreamInfoEnabled)
                Input.FindStreamInfo(Options.CodecOptions);

            if (Input.IO.IsValid())
                Input.IO.EndOfStream = false; // FIXME hack, ffplay maybe should not use avio_feof() to test for the end

            if (Options.IsByteSeekingEnabled.IsAuto())
            {
                Options.IsByteSeekingEnabled = Input.InputFormat.Flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) &&
                    !Input.InputFormat.ShortNames.Any(c => c == "ogg") ? ThreeState.On : ThreeState.Off;
            }

            MaxPictureDuration = Input.InputFormat.Flags.HasFlag(ffmpeg.AVFMT_TS_DISCONT) ? 10.0 : 3600.0;

            var metadata = Input.Metadata;
            if (metadata.ContainsKey("title"))
                Title = $"{metadata["title"]} - {Options.InputFileName}";

            // if seeking requested, we execute it
            if (Options.StartOffset.IsValidTimestamp())
            {
                var startTimestamp = Options.StartOffset;
                // add the stream start time
                if (Input.StartTime.IsValidTimestamp())
                    startTimestamp += Input.StartTime;

                var seekStoStartResult = Input.SeekFile(long.MinValue, startTimestamp, long.MaxValue);
                if (seekStoStartResult < 0)
                    ($"{FileName}: could not seek to position {(startTimestamp / Clock.TimeBaseMicros)}.").LogWarning();
            }

            IsRealTime = Input.IsRealTime;

            if (Options.ShowStatus != 0)
                Input.DumpFormat(FileName);
        }
        catch
        {
            ($"{FileName}: Preparing input context failed.").LogError();
            throw;
        }
        finally
        {
            formatOptions.Dispose();
        }
    }

    private void OpenComponents()
    {
        var streamIndexes = new MediaTypeDictionary<int>(-1);
        for (var i = 0; i < Input.Streams.Count; i++)
        {
            var stream = Input.Streams[i];
            var mediaType = stream.CodecParameters.CodecType;
            stream.DiscardFlags = AVDiscard.AVDISCARD_ALL;

            var hasStreamSpec = mediaType >= 0 &&
                Options.WantedStreams.ContainsKey(mediaType) &&
                Options.WantedStreams[mediaType] is not null &&
                !streamIndexes.HasValue(mediaType);

            var isStreamSpecMatch = hasStreamSpec &&
                Input.MatchStreamSpecifier(stream, Options.WantedStreams[mediaType]) > 0;

            if (hasStreamSpec && isStreamSpecMatch)
                streamIndexes[mediaType] = i;
        }

        foreach (var mediaType in MediaTypeDictionary<int>.MediaTypes)
        {
            if (!Options.WantedStreams.ContainsKey(mediaType) || Options.WantedStreams[mediaType] is null)
                continue;

            if (streamIndexes[mediaType] != streamIndexes.DefaultValue)
                continue;

            ($"Stream specifier {Options.WantedStreams[mediaType]} does not match any {mediaType.ToName()} stream").LogError();
            streamIndexes[mediaType] = int.MaxValue;
        }

        if (!Options.IsVideoDisabled)
            streamIndexes.Video = Input.FindBestVideoStream(streamIndexes.Video);

        if (!Options.IsAudioDisabled)
            streamIndexes.Audio = Input.FindBestAudioStream(streamIndexes.Audio, streamIndexes.Video);

        if (!Options.IsVideoDisabled && !Options.IsSubtitleDisabled)
            streamIndexes.Subtitle = Input.FindBestSubtitleStream(streamIndexes.Subtitle,
                streamIndexes.HasAudio ? streamIndexes.Audio : streamIndexes.Video);

        ShowMode = Options.ShowMode;
        if (streamIndexes.Video >= 0)
        {
            var st = Input.Streams[streamIndexes.Video];
            var codecpar = st.CodecParameters;
            var sar = Input.GuessAspectRatio(st);
            if (codecpar.Width != 0)
                Presenter.UpdatePictureSize(codecpar.Width, codecpar.Height, sar);
        }

        // open the streams
        if (streamIndexes.HasAudio)
            OpenComponent(streamIndexes.Audio);

        try
        {
            OpenComponent(streamIndexes.Video);
            if (ShowMode == ShowMode.None)
                ShowMode = ShowMode.Video;
        }
        catch
        {
            throw;
        }

        if (streamIndexes.HasSubtitle)
            OpenComponent(streamIndexes.Subtitle);

        if (Video.StreamIndex < 0 && Audio.StreamIndex < 0)
        {
            ($"Failed to open file '{FileName}' or configure filtergraph.").LogFatal();
            throw new InvalidOperationException($"Failed to open file '{FileName}' or configure filtergraph");
        }

        if (Options.IsInfiniteBufferEnabled.IsAuto() && IsRealTime)
            Options.IsInfiniteBufferEnabled = ThreeState.On;
    }

    /// <summary>
    /// This thread gets the stream from the disk or the network
    /// </summary>
    private void ReadingThreadMethod()
    {
        try
        {
            PrepareInput();
            OpenComponents();

            var isRstpStream = Input.InputFormat?.ShortNames.Any(c => c.ToUpperInvariant().Equals("RTSP", StringComparison.Ordinal)) ?? false;
            var isMmshProtocol = Options.InputFileName?.StartsWith("mmsh:", StringComparison.OrdinalIgnoreCase) ?? false;

            while (true)
            {
                if (IsAbortRequested)
                    break;

                if (IsPaused != WasPaused)
                {
                    WasPaused = IsPaused;
                    if (IsPaused)
                        ReadPauseResultCode = Input.ReadPause();
                    else
                        Input.ReadPlay();
                }

                if (IsPaused && (isRstpStream || (Input.IO.IsValid() && isMmshProtocol)))
                {
                    // wait 10 ms to avoid trying to get another packet
                    // XXX: horrible
                    // SDL.SDL_Delay(10); // or use ffmpeg.av_usleep(10000);
                    ffmpeg.av_usleep(10000);
                    continue;
                }

                if (IsSeekRequested)
                    InputContextHandleSeek();

                if (IsPictureAttachmentPending)
                {
                    if (Video.IsPictureAttachmentStream)
                    {
                        var copy = Video.Stream.CloneAttachedPicture();
                        Video.Packets.Enqueue(copy);
                        Video.Packets.EnqueueNull(Video.StreamIndex);
                    }

                    IsPictureAttachmentPending = false;
                }

                // if the queue are full, no need to read more
                if (Options.IsInfiniteBufferEnabled is not ThreeState.On && (HasEnoughPacketBuffer || HasEnoughPacketCount))
                {
                    // wait 10 ms
                    NeedsMorePacketsEvent.Wait(Constants.WaitTimeout);
                    continue;
                }

                if (!IsPaused && Audio.HasFinishedDecoding && Video.HasFinishedDecoding)
                {
                    if (Options.LoopCount is not 1 && (Options.LoopCount is 0 || (--Options.LoopCount) > 0))
                    {
                        SeekByTimestamp(Options.StartOffset.IsValidTimestamp() ? Options.StartOffset : 0);
                    }
                    else if (Options.ExitOnFinish)
                    {
                        break;
                    }
                }

                if (!ReadPacket())
                    break;
            }
        }
        catch (Exception ex)
        {
            Input?.Dispose();
            Input = default;
            Presenter.HandleFatalException(ex);
        }
    }

    private void InputContextHandleSeek()
    {
        var seekTarget = SeekAbsoluteTarget;
        var seekTargetMin = SeekRelativeTarget > 0 ? seekTarget - SeekRelativeTarget + 2 : long.MinValue;
        var seekTargetMax = SeekRelativeTarget < 0 ? seekTarget - SeekRelativeTarget - 2 : long.MaxValue;
        // FIXME the +-2 is due to rounding being not done in the correct direction in generation
        //      of the seek_pos/seek_rel variables

        var resultCode = Input.SeekFile(seekTargetMin, seekTarget, seekTargetMax, SeekFlags);
        if (resultCode < 0)
        {
            ($"{Input.Url}: error while seeking").LogError();
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

    private bool ReadPacket()
    {
        var resultCode = Input.ReadFrame(out var readPacket);

        if (resultCode < 0)
        {
            readPacket.Dispose();

            if (!IsAtEndOfStream && (resultCode == ffmpeg.AVERROR_EOF || Input.IO.TestEndOfStream()))
            {
                foreach (var c in Components)
                {
                    if (c.StreamIndex >= 0)
                        c.Packets.EnqueueNull(c.StreamIndex);
                }

                IsAtEndOfStream = true;
            }

            if (Input.IO.IsValid() && Input.IO.Error != 0)
                return false;

            NeedsMorePacketsEvent.Wait(Constants.WaitTimeout);
            return true;
        }
        else
        {
            IsAtEndOfStream = false;
        }

        // check if packet is in play range specified by user, then queue, otherwise discard.
        var startOffset = Options.StartOffset.IsValidTimestamp() ? Options.StartOffset : 0;
        var streamTimeBase = Input.Streams[readPacket.StreamIndex].TimeBase.ToFactor();
        var streamStartPts = Input.Streams[readPacket.StreamIndex].StartTime;
        streamStartPts = streamStartPts.IsValidTimestamp() ? streamStartPts : 0;
        var packetPtsOffset = readPacket.PtsUnits - streamStartPts;

        var isPacketInPlayRange = !Options.Duration.IsValidTimestamp() ||
                (packetPtsOffset * streamTimeBase) - (startOffset / Clock.TimeBaseMicros)
                <= (Options.Duration / Clock.TimeBaseMicros);

        if (isPacketInPlayRange && FindComponentByStreamIndex(readPacket.StreamIndex) is MediaComponent component)
            component.Packets.Enqueue(readPacket);
        else
            readPacket.Dispose();

        return true;
    }
}
