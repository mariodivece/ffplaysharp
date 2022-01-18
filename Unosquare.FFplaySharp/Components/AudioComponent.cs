namespace Unosquare.FFplaySharp.Components;

public unsafe sealed class AudioComponent : FilteringMediaComponent, ISerialGroupable
{
    private readonly double SyncDiffAverageCoffiecient = Math.Exp(Math.Log(0.01) / Constants.AudioDiffAveragesCount);

    private ResamplerContext ConvertContext;
    private ByteBuffer ResampledOutputBuffer;
    private long StartPts;
    private AVRational StartPtsTimeBase;
    private long NextPts;
    private AVRational NextPtsTimeBase;
    private double LastFrameTime;
    private double SyncDiffTotalDelay; /* used for AV difference average computation */
    private double SyncDiffDelayThreshold;
    private int SyncDiffAverageCount;
    private AudioParams StreamSpec = new();
    private AudioParams FilterSpec = new();

    public AudioComponent(MediaContainer container)
        : base(container)
    {
        // placeholder
    }

    /// <summary>
    /// Gets or sets the Frame Time (ported from audio_clock)
    /// </summary>
    public double FrameTime { get; private set; }

    public int GroupIndex { get; private set; } = -1;

    public override string? WantedCodecName => Container.Options.AudioForcedCodecName;

    public AudioParams HardwareSpec { get; private set; } = new();

    public override AVMediaType MediaType { get; } = AVMediaType.AVMEDIA_TYPE_AUDIO;

    public void ConfigureFilters(FFCodecContext codecContext)
    {
        FilterSpec.ImportFrom(codecContext);
        ConfigureFilters(false);
    }

    public void ConfigureFilters(bool forceOutputFormat)
    {
        try
        {
            var outputSampleFormats = new[] { (int)AVSampleFormat.AV_SAMPLE_FMT_S16 };
            ReallocateFilterGraph();
            var resamplerOptions = RetrieveResamplerOptions();

            if (!string.IsNullOrEmpty(resamplerOptions))
                FilterGraph.SetOption("aresample_swr_opts", resamplerOptions);

            var sourceBufferOptions = $"sample_rate={FilterSpec.SampleRate}:sample_fmt={FilterSpec.SampleFormatName}:" +
                $"channels={FilterSpec.Channels}:time_base={1}/{FilterSpec.SampleRate}";

            if (FilterSpec.ChannelLayout != 0)
                sourceBufferOptions = $"{sourceBufferOptions}:channel_layout=0x{FilterSpec.ChannelLayout:x16}";

            var inputFilterContext = FFFilterContext.Create(FilterGraph, "abuffer", "audioSourceBuffer", sourceBufferOptions);
            var outputFilterContext = FFFilterContext.Create(FilterGraph, "abuffersink", "audioSinkBuffer");

            outputFilterContext.SetOptionList("sample_fmts", outputSampleFormats);
            outputFilterContext.SetOption("all_channel_counts", 1);

            if (forceOutputFormat)
            {
                var outputChannelLayout = new[] { HardwareSpec.ChannelLayout };
                var outputChannelCount = new[] { HardwareSpec.ChannelLayout != 0 ? -1 : HardwareSpec.Channels };
                var outputSampleRates = new[] { HardwareSpec.SampleRate };

                outputFilterContext.SetOption("all_channel_counts", 0);
                outputFilterContext.SetOptionList("channel_layouts", outputChannelLayout);
                outputFilterContext.SetOptionList("channel_counts", outputChannelCount);
                outputFilterContext.SetOptionList("sample_rates", outputSampleRates);
            }

            MaterializeFilterGraph(Container.Options.AudioFilterGraphs, inputFilterContext, outputFilterContext);
            InputFilter = inputFilterContext;
            OutputFilter = outputFilterContext;
        }
        catch
        {
            ReleaseFilterGraph();
            throw;
        }
    }

    /// <summary>
    /// Decode one audio frame and return its uncompressed size.
    /// The processed audio frame is decoded, converted if required, and
    /// stored in is->audio_buf, with size in bytes given by the return value.
    /// </summary>
    /// <returns>The size in bytes of the output buffer.</returns>
    public BufferReference RefillOutputBuffer()
    {
        var result = new BufferReference(null, -1);
        FrameHolder? audio;

        if (Container.IsPaused)
            return result;

        do
        {
            var callbackTimeout = (double)HardwareSpec.BufferSize / HardwareSpec.BytesPerSecond / 2.0;

            while (Frames.PendingCount == 0)
            {
                var elapsedCallback = Clock.SystemTime - Container.Renderer.Audio.LastCallbackTime;
                if (elapsedCallback > callbackTimeout)
                    return result;

                ffmpeg.av_usleep(1000);
            }

            if ((audio = Frames.PeekWaitCurrent()) is null)
                return result;

            Frames.Dequeue();

        } while (audio.GroupIndex != Packets.GroupIndex);

        var wantedSampleCount = SyncWantedSamples(audio.Frame.SampleCount);

        if (audio.Frame.SampleFormat != StreamSpec.SampleFormat ||
            audio.Frame.ChannelLayout != StreamSpec.ChannelLayout ||
            audio.Frame.SampleRate != StreamSpec.SampleRate ||
            (wantedSampleCount != audio.Frame.SampleCount && ConvertContext.IsNull()))
        {
            ReleaseConvertContext();
            ConvertContext = new(
                HardwareSpec.ChannelLayout,
                HardwareSpec.SampleFormat,
                HardwareSpec.SampleRate,
                audio.Frame.ChannelLayout,
                audio.Frame.SampleFormat,
                audio.Frame.SampleRate);

            if (ConvertContext.IsNull() || ConvertContext.Initialize() < 0)
            {
                ($"Cannot create sample rate converter for conversion of {audio.Frame.SampleRate} Hz " +
                $"{audio.Frame.SampleFormatName} {audio.Frame.Channels} channels to " +
                $"{HardwareSpec.SampleRate} Hz {HardwareSpec.SampleFormatName} {HardwareSpec.Channels} channels!")
                .LogError();

                ReleaseConvertContext();
                return result;
            }

            StreamSpec.ImportFrom(audio.Frame);
            StreamSpec.ChannelLayout = audio.Frame.ChannelLayout;
        }

        int resampledBufferSize;

        if (ConvertContext.IsNotNull())
        {
            var wantedOutputSize = wantedSampleCount * HardwareSpec.SampleRate / audio.Frame.SampleRate + 256;
            var outputBufferSize = AudioParams.ComputeSamplesBufferSize(
                HardwareSpec.Channels, wantedOutputSize, HardwareSpec.SampleFormat, false);

            if (outputBufferSize < 0)
            {
                ("av_samples_get_buffer_size() failed").LogError();
                return result;
            }

            if (wantedSampleCount != audio.Frame.SampleCount)
            {
                var compensationDelta = (wantedSampleCount - audio.Frame.SampleCount) * HardwareSpec.SampleRate / audio.Frame.SampleRate;
                var compensationDistance = wantedSampleCount * HardwareSpec.SampleRate / audio.Frame.SampleRate;

                if (ConvertContext.SetCompensation(compensationDelta, compensationDistance) < 0)
                {
                    ("swr_set_compensation() failed").LogError();
                    return result;
                }
            }

            ResampledOutputBuffer = ByteBuffer.Reallocate(ResampledOutputBuffer, (ulong)outputBufferSize);
            var audioBufferIn = audio.Frame.ExtendedData;
            var audioBufferOut = ResampledOutputBuffer.Target;
            var outputSampleCount = ConvertContext.Convert(
                &audioBufferOut, wantedOutputSize, audioBufferIn, audio.Frame.SampleCount);

            ResampledOutputBuffer.Update(audioBufferOut);
            audio.Frame.ExtendedData = audioBufferIn;

            if (outputSampleCount < 0)
            {
                ("swr_convert() failed").LogError();
                return result;
            }

            if (outputSampleCount == wantedOutputSize)
            {
                ("Audio buffer is probably too small.").LogWarning();
                if (ConvertContext.Initialize() < 0)
                    ReleaseConvertContext();
            }

            result.Update(ResampledOutputBuffer.Target);
            resampledBufferSize = outputSampleCount * HardwareSpec.Channels * HardwareSpec.BytesPerSample;
        }
        else
        {
            result.Update(audio.Frame.Data[0]);
            resampledBufferSize = audio.Frame.SamplesBufferSize;
        }

        // Used for debugging message below
        var currentAudioTime = FrameTime;

        // update the audio clock with the pts
        FrameTime = audio.HasValidTime
            ? audio.Time + audio.Frame.AudioComputedDuration
            : double.NaN;

        GroupIndex = audio.GroupIndex;
        if (false && Debugger.IsAttached)
        {
            Console.WriteLine(
                $"audio: delay={(FrameTime - LastFrameTime),-8:0.####} clock={FrameTime,-8:0.####} " +
                $"clock0={currentAudioTime,-8:0.####}");
        }

        LastFrameTime = FrameTime;
        result.Length = resampledBufferSize;
        return result;
    }

    /// <summary>
    /// Port of synchronize_audio
    /// Return the wanted number of samples to get better sync if sync_type
    /// is video or external master clock.
    /// </summary>
    /// <param name="sampleCount">The number of samples contained in the frame.</param>
    /// <returns>The wanted sample count for synchronized output.</returns>
    public int SyncWantedSamples(int sampleCount)
    {
        var wantedSampleCount = sampleCount;

        // if not master, then we try to remove or add samples to correct the clock.
        if (Container.MasterSyncMode == ClockSource.Audio)
            return wantedSampleCount;

        var clockDelay = Container.AudioClock.Value - Container.MasterTime;

        if (!clockDelay.IsNaN() && Math.Abs(clockDelay) < Constants.MediaNoSyncThreshold)
        {
            SyncDiffTotalDelay = clockDelay + (SyncDiffAverageCoffiecient * SyncDiffTotalDelay);
            if (SyncDiffAverageCount < Constants.AudioDiffAveragesCount)
            {
                // not enough measures to have a correct estimate.
                SyncDiffAverageCount++;
            }
            else
            {
                // estimate the A-V difference.
                var syncDiffDelay = SyncDiffTotalDelay * (1.0 - SyncDiffAverageCoffiecient);

                if (Math.Abs(syncDiffDelay) >= SyncDiffDelayThreshold)
                {
                    wantedSampleCount = sampleCount + (int)(clockDelay * StreamSpec.SampleRate);
                    var minSampleCount = (int)(sampleCount * (100 - Constants.SampleCorrectionPercentMax) / 100);
                    var maxSampleCount = (int)(sampleCount * (100 + Constants.SampleCorrectionPercentMax) / 100);
                    wantedSampleCount = wantedSampleCount.Clamp(minSampleCount, maxSampleCount);
                }

                ($"diff={clockDelay} adiff={syncDiffDelay} sample_diff={(wantedSampleCount - sampleCount)} " +
                $"apts={FrameTime} {SyncDiffDelayThreshold}.")
                .LogTrace();
            }
        }
        else
        {
            // too big difference: may be initial PTS errors, so reset A-V filter.
            SyncDiffAverageCount = 0;
            SyncDiffTotalDelay = 0;
        }

        return wantedSampleCount;
    }

    public override void Close()
    {
        if (StreamIndex < 0 || StreamIndex >= Container.Input.Streams.Count)
            return;

        AbortDecoder();
        Container.Renderer.Audio.Close();
        DisposeDecoder();

        ReleaseConvertContext();

        ResampledOutputBuffer?.Release();
        ResampledOutputBuffer = default;

        Container.Input.Streams[StreamIndex].DiscardFlags = AVDiscard.AVDISCARD_ALL;
        Stream = default;
        StreamIndex = -1;
    }

    protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.AudioFrameQueueCapacity, true);

    public override void InitializeDecoder(FFCodecContext codecContext, int streamIndex)
    {
        ConfigureFilters(codecContext);

        var wantedSpec = AudioParams.FromFilterContext(OutputFilter);
        var audioHardwareSpec = Container.Renderer.Audio.Open(wantedSpec);
        if (audioHardwareSpec.BufferSize < 0)
            throw new FFmpegException(-1, "Could not initialize audio hardware buffer.");

        HardwareSpec = audioHardwareSpec.Clone();

        // Start off with the source spec to be the same as the hardware
        // spec as no frames have been decoded yet. This spec will change
        // as audio frames become available.
        StreamSpec = HardwareSpec.Clone();

        // init averaging filter
        SyncDiffAverageCount = 0;
        SyncDiffTotalDelay = 0;

        // since we do not have a precise anough audio FIFO fullness,
        // we correct audio sync only if larger than this threshold.
        SyncDiffDelayThreshold = (double)HardwareSpec.BufferSize / HardwareSpec.BytesPerSecond;

        base.InitializeDecoder(codecContext, streamIndex);

        if (Container.Input.IsSeekMethodUnknown)
        {
            StartPts = Stream.StartTime;
            StartPtsTimeBase = Stream.TimeBase;
        }
        else
        {
            StartPts = ffmpeg.AV_NOPTS_VALUE;
            StartPtsTimeBase = ffmpeg.av_make_q(0, 0);
        }
    }

    private string RetrieveResamplerOptions()
    {
        var result = string.Join(":", Container.Options.ResamplerOptions.Select(kvp => $"{kvp.Key}={kvp.Value}").ToArray());
        return string.IsNullOrWhiteSpace(result) ? string.Empty : result;
    }

    private void ReleaseConvertContext()
    {
        ConvertContext?.Release();
        ConvertContext = default;
    }

    private int DecodeFrame(FFFrame frame)
    {
        var resultCode = DecodeFrame(frame, null);
        if (resultCode >= 0)
        {
            var decoderTimeBase = ffmpeg.av_make_q(1, frame.SampleRate);

            if (frame.Pts.IsValidPts())
                frame.Pts = ffmpeg.av_rescale_q(frame.Pts, CodecContext.PacketTimeBase, decoderTimeBase);
            else if (NextPts.IsValidPts())
                frame.Pts = ffmpeg.av_rescale_q(NextPts, NextPtsTimeBase, decoderTimeBase);

            if (frame.Pts.IsValidPts())
            {
                NextPts = frame.Pts + frame.SampleCount;
                NextPtsTimeBase = decoderTimeBase;
            }
        }

        return resultCode;
    }

    protected override void FlushCodecBuffers()
    {
        base.FlushCodecBuffers();
        NextPts = StartPts;
        NextPtsTimeBase = StartPtsTimeBase;
    }

    protected override void DecodingThreadMethod()
    {
        var lastPacketGroupIndex = -1;
        var resultCode = 0;

        var decodedFrame = new FFFrame();

        do
        {
            var gotSamples = DecodeFrame(decodedFrame);

            if (gotSamples < 0)
                break;

            if (gotSamples == 0)
                continue;

            var decoderChannelLayout = AudioParams.ValidateChannelLayout(decodedFrame.ChannelLayout, decodedFrame.Channels);
            var needsDifferentSpec = FilterSpec.IsDifferentTo(decodedFrame) ||
                FilterSpec.ChannelLayout != decoderChannelLayout ||
                FilterSpec.SampleRate != decodedFrame.SampleRate ||
                PacketGroupIndex != lastPacketGroupIndex;

            if (needsDifferentSpec)
            {
                var decoderLayoutString = AudioParams.GetChannelLayoutString(decoderChannelLayout);

                ($"Audio frame changed from " +
                $"rate:{FilterSpec.SampleRate} ch:{FilterSpec.Channels} fmt:{FilterSpec.SampleFormatName} " +
                $"layout:{FilterSpec.ChannelLayoutString} serial:{lastPacketGroupIndex} to " +
                $"rate:{decodedFrame.SampleRate} ch:{decodedFrame.Channels} " +
                $"fmt:{AudioParams.GetSampleFormatName(decodedFrame.SampleFormat)} layout:{decoderLayoutString} " +
                $"serial:{PacketGroupIndex}.")
                .LogDebug();

                FilterSpec.ImportFrom(decodedFrame);
                lastPacketGroupIndex = PacketGroupIndex;

                try
                {
                    ConfigureFilters(true);
                }
                catch
                {
                    break;
                }
            }

            if (EnqueueFilteringFrame(decodedFrame) < 0)
                break;

            var isFrameQueueAvailable = true;
            while ((resultCode = DequeueFilteringFrame(decodedFrame)) >= 0)
            {
                var queuedFrame = Frames.PeekWriteable();

                if (queuedFrame is null)
                {
                    isFrameQueueAvailable = false;
                    break;
                }

                var frameTime = decodedFrame.Pts.IsValidPts() ? decodedFrame.Pts * OutputFilterTimeBase.ToFactor() : double.NaN;
                var frameDuration = ffmpeg.av_make_q(decodedFrame.SampleCount, decodedFrame.SampleRate).ToFactor();
                queuedFrame.Update(decodedFrame, PacketGroupIndex, frameTime, frameDuration);
                Frames.Enqueue();

                if (Packets.GroupIndex != PacketGroupIndex)
                    break;
            }

            if (!isFrameQueueAvailable)
                break;

            if (resultCode == ffmpeg.AVERROR_EOF)
                FinalPacketGroupIndex = PacketGroupIndex;

        } while (resultCode >= 0 || resultCode == ffmpeg.AVERROR(ffmpeg.EAGAIN) || resultCode == ffmpeg.AVERROR_EOF);

        // Ported as the_end section.
        ReleaseFilterGraph();
        decodedFrame.Release();
    }
}
