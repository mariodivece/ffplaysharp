namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using System;
    using System.Diagnostics;
    using System.Linq;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class AudioComponent : FilteringMediaComponent, ISerialGroupable
    {
        private readonly double SyncDiffAverageCoffiecient = Math.Exp(Math.Log(0.01) / Constants.AUDIO_DIFF_AVG_NB);

        private byte* ResampledOutputBuffer;
        private uint ResampledOutputBufferSize;
        private long StartPts;
        private AVRational StartPtsTimeBase;
        private long NextPts;
        private AVRational NextPtsTimeBase;
        private double LastFrameTime = 0;
        private double SyncDiffTotalDelay; /* used for AV difference average computation */
        private double SyncDiffDelayThreshold;
        private int SyncDiffAverageCount;

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

        public byte* OutputBuffer { get; set; }

        public int HardwareBufferSize { get; private set; }

        public override string WantedCodecName => Container.Options.AudioForcedCodecName;

        public SwrContext* ConvertContext { get; private set; }

        /// <summary>
        /// Gets the audio parameters as specified by decoded frames.
        /// </summary>
        public AudioParams StreamSpec { get; private set; } = new();

        public AudioParams FilterSpec { get; } = new();

        public AudioParams HardwareSpec { get; private set; } = new();

        public override AVMediaType MediaType { get; } = AVMediaType.AVMEDIA_TYPE_AUDIO;

        public int ConfigureFilters(FFCodecContext codecContext)
        {
            FilterSpec.ImportFrom(codecContext);
            return ConfigureFilters(false);
        }

        public int ConfigureFilters(bool forceOutputFormat)
        {
            var outputSampleFormats = new[] { (int)AVSampleFormat.AV_SAMPLE_FMT_S16 };
            ReallocateFilterGraph();
            var resamplerOptions = RetrieveResamplerOptions();
            int resultCode;

            FilterGraph.SetOption("aresample_swr_opts", resamplerOptions);
            var sourceBufferOptions = $"sample_rate={FilterSpec.SampleRate}:sample_fmt={FilterSpec.SampleFormatName}:" +
                $"channels={FilterSpec.Channels}:time_base={1}/{FilterSpec.SampleRate}";

            if (FilterSpec.Layout != 0)
                sourceBufferOptions = $"{sourceBufferOptions}:channel_layout=0x{FilterSpec.Layout:x16}";

            FFFilterContext inputFilterContext;
            (inputFilterContext, resultCode) = FFFilterContext.Create(
                FilterGraph, FFFilter.FromName("abuffer"), "audioSourceBuffer", sourceBufferOptions);

            if (resultCode < 0)
                goto end;

            FFFilterContext outputFilterContext;
            (outputFilterContext, resultCode) = FFFilterContext.Create(
                FilterGraph, FFFilter.FromName("abuffersink"), "audioSinkBuffer", null);

            if (resultCode < 0)
                goto end;

            if ((resultCode = outputFilterContext.SetOptionList("sample_fmts", outputSampleFormats)) < 0)
                goto end;

            if ((resultCode = outputFilterContext.SetOption("all_channel_counts", 1)) < 0)
                goto end;

            if (forceOutputFormat)
            {
                var outputChannelLayout = new[] { HardwareSpec.Layout };
                var outputChannelCount = new[] { HardwareSpec.Layout != 0 ? -1 : HardwareSpec.Channels };
                var outputSampleRates = new[] { HardwareSpec.SampleRate };

                if ((resultCode = outputFilterContext.SetOption("all_channel_counts", 0)) < 0)
                    goto end;
                if ((resultCode = outputFilterContext.SetOptionList("channel_layouts", outputChannelLayout)) < 0)
                    goto end;
                if ((resultCode = outputFilterContext.SetOptionList("channel_counts", outputChannelCount)) < 0)
                    goto end;
                if ((resultCode = outputFilterContext.SetOptionList("sample_rates", outputSampleRates)) < 0)
                    goto end;
            }

            resultCode = MaterializeFilterGraph(Container.Options.AudioFilterGraphs, inputFilterContext, outputFilterContext);

            if (resultCode < 0) goto end;

            InputFilter = inputFilterContext;
            OutputFilter =outputFilterContext;

            end:
            if (resultCode < 0)
                ReleaseFilterGraph();

            return resultCode;
        }


        /// <summary>
        /// Decode one audio frame and return its uncompressed size.
        /// The processed audio frame is decoded, converted if required, and
        /// stored in is->audio_buf, with size in bytes given by the return value.
        /// </summary>
        /// <returns>The size in bytes of the output buffer.</returns>
        public int RefillOutputBuffer()
        {
            FrameHolder af;

            if (Container.IsPaused)
                return -1;

            do
            {
                while (Frames.PendingCount == 0)
                {
                    var threshold = Clock.TimeBaseMicros * HardwareBufferSize / HardwareSpec.BytesPerSecond / 2.0;
                    if ((Clock.SystemTime - Container.Renderer.Audio.AudioCallbackTime) > threshold)
                        return -1;

                    ffmpeg.av_usleep(1000);
                }

                if ((af = Frames.PeekWaitCurrent()) == null)
                    return -1;

                Frames.Dequeue();

            } while (af.GroupIndex != Packets.GroupIndex);

            var frameBufferSize = ffmpeg.av_samples_get_buffer_size(null, af.Channels, af.SampleCount, af.SampleFormat, 1);
            var frameChannelLayout = AudioParams.ComputeChannelLayout(af.FramePtr);
            var wantedSampleCount = SyncWantedSamples(af.SampleCount);

            if (af.SampleFormat != StreamSpec.SampleFormat ||
                frameChannelLayout != StreamSpec.Layout ||
                af.Frequency != StreamSpec.SampleRate ||
                (wantedSampleCount != af.SampleCount && ConvertContext == null))
            {
                ReleaseConvertContext();
                ConvertContext = ffmpeg.swr_alloc_set_opts(null,
                    HardwareSpec.Layout, HardwareSpec.SampleFormat, HardwareSpec.SampleRate,
                    frameChannelLayout, af.SampleFormat, af.Frequency,
                    0, null);

                if (ConvertContext == null || ffmpeg.swr_init(ConvertContext) < 0)
                {
                    Helpers.LogError(
                           $"Cannot create sample rate converter for conversion of {af.Frequency} Hz " +
                           $"{af.SampleFormatName} {af.Channels} channels to " +
                           $"{HardwareSpec.SampleRate} Hz {HardwareSpec.SampleFormatName} {HardwareSpec.Channels} channels!\n");

                    ReleaseConvertContext();
                    return -1;
                }

                StreamSpec.ImportFrom(af.FramePtr);
                StreamSpec.Layout = frameChannelLayout;
            }

            var resampledBufferSize = 0;

            if (ConvertContext != null)
            {
                var wantedOutputSize = wantedSampleCount * HardwareSpec.SampleRate / af.Frequency + 256;
                var outputBufferSize = ffmpeg.av_samples_get_buffer_size(null, HardwareSpec.Channels, wantedOutputSize, HardwareSpec.SampleFormat, 0);

                if (outputBufferSize < 0)
                {
                    Helpers.LogError("av_samples_get_buffer_size() failed\n");
                    return -1;
                }
                if (wantedSampleCount != af.SampleCount)
                {
                    if (ffmpeg.swr_set_compensation(ConvertContext, (wantedSampleCount - af.SampleCount) * HardwareSpec.SampleRate / af.Frequency,
                                                wantedSampleCount * HardwareSpec.SampleRate / af.Frequency) < 0)
                    {
                        Helpers.LogError("swr_set_compensation() failed\n");
                        return -1;
                    }
                }

                if (ResampledOutputBuffer == null)
                {
                    ResampledOutputBuffer = (byte*)ffmpeg.av_mallocz((ulong)outputBufferSize);
                    ResampledOutputBufferSize = (uint)outputBufferSize;
                }

                if (ResampledOutputBufferSize < outputBufferSize && ResampledOutputBuffer != null)
                {
                    ffmpeg.av_free(ResampledOutputBuffer);
                    ResampledOutputBuffer = (byte*)ffmpeg.av_mallocz((ulong)outputBufferSize);
                    ResampledOutputBufferSize = (uint)outputBufferSize;
                }

                var audioBufferIn = af.FramePtr.ExtendedData;
                var audioBufferOut = ResampledOutputBuffer;
                var outputSampleCount = ffmpeg.swr_convert(ConvertContext, &audioBufferOut, wantedOutputSize, audioBufferIn, af.SampleCount);
                ResampledOutputBuffer = audioBufferOut;
                af.FramePtr.ExtendedData = audioBufferIn;

                if (outputSampleCount < 0)
                {
                    Helpers.LogError("swr_convert() failed\n");
                    return -1;
                }
                if (outputSampleCount == wantedOutputSize)
                {
                    Helpers.LogWarning("audio buffer is probably too small\n");
                    if (ffmpeg.swr_init(ConvertContext) < 0)
                    {
                        ReleaseConvertContext();
                    }
                }

                OutputBuffer = ResampledOutputBuffer;
                resampledBufferSize = outputSampleCount * HardwareSpec.Channels * HardwareSpec.BytesPerSample;
            }
            else
            {
                OutputBuffer = af.FramePtr.Data[0];
                resampledBufferSize = frameBufferSize;
            }

            var currentFrameTime = FrameTime;

            // update the audio clock with the pts
            FrameTime = af.HasValidTime ? af.Time + (double)af.SampleCount / af.Frequency : double.NaN;

            GroupIndex = af.GroupIndex;
            if (Debugger.IsAttached)
            {
                // Console.WriteLine($"audio: delay={(FrameTime - LastFrameTime),-8:0.####} clock={FrameTime,-8:0.####} clock0={currentFrameTime,-8:0.####}");
            }

            LastFrameTime = FrameTime;
            return resampledBufferSize;
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
            if (Container.MasterSyncMode == ClockSync.Audio)
                return wantedSampleCount;

            var clockDelay = Container.AudioClock.Value - Container.MasterTime;

            if (!clockDelay.IsNaN() && Math.Abs(clockDelay) < Constants.AV_NOSYNC_THRESHOLD)
            {
                SyncDiffTotalDelay = clockDelay + (SyncDiffAverageCoffiecient * SyncDiffTotalDelay);
                if (SyncDiffAverageCount < Constants.AUDIO_DIFF_AVG_NB)
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
                        var minSampleCount = (int)(sampleCount * (100 - Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100);
                        var maxSampleCount = (int)(sampleCount * (100 + Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100);
                        wantedSampleCount = wantedSampleCount.Clamp(minSampleCount, maxSampleCount);
                    }

                    Helpers.LogTrace($"diff={clockDelay} adiff={syncDiffDelay} sample_diff={(wantedSampleCount - sampleCount)} apts={FrameTime} {SyncDiffDelayThreshold}\n");
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
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext.Streams.Count)
                return;

            AbortDecoder();
            Container.Renderer.Audio.Close();
            DisposeDecoder();

            ReleaseConvertContext();

            if (ResampledOutputBuffer != null)
                ffmpeg.av_free(ResampledOutputBuffer);

            ResampledOutputBuffer = null;
            ResampledOutputBufferSize = 0;
            OutputBuffer = null;

            Container.InputContext.Streams[StreamIndex].DiscardFlags = AVDiscard.AVDISCARD_ALL;
            Stream = null;
            StreamIndex = -1;
        }

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.AudioFrameQueueCapacity, true);

        public override unsafe int InitializeDecoder(FFCodecContext codecContext, int streamIndex)
        {
            if (ConfigureFilters(codecContext) < 0)
                return -1;

            var wantedSpec = AudioParams.FromFilterContext(OutputFilter);
            var hardwareBufferSize = Container.Renderer.Audio.Open(wantedSpec, out var audioHardwareSpec);
            if (hardwareBufferSize < 0)
                return -1;

            HardwareBufferSize = hardwareBufferSize;
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
            SyncDiffDelayThreshold = (double)HardwareBufferSize / HardwareSpec.BytesPerSecond;

            base.InitializeDecoder(codecContext, streamIndex);

            if (Container.IsSeekMethodUnknown)
            {
                StartPts = Stream.StartTime;
                StartPtsTimeBase = Stream.TimeBase;
            }
            else
            {
                StartPts = ffmpeg.AV_NOPTS_VALUE;
                StartPtsTimeBase = ffmpeg.av_make_q(0, 0);
            }

            return 0;
        }

        private string RetrieveResamplerOptions()
        {
            var result = string.Join(":", Container.Options.ResamplerOptions.Select(kvp => $"{kvp.Key}={kvp.Value}").ToArray());
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        private void ReleaseConvertContext()
        {
            var convertContext = ConvertContext;
            ffmpeg.swr_free(&convertContext);
            ConvertContext = null;
        }

        private int DecodeFrame(FFFrame frame)
        {
            var resultCode = DecodeFrame(frame, out _);
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
            var gotSamples = 0;
            var resultCode = 0;

            var decodedFrame = new FFFrame();

            do
            {
                gotSamples = DecodeFrame(decodedFrame);

                if (gotSamples < 0)
                    break;

                if (gotSamples == 0)
                    continue;

                var decoderChannelLayout = AudioParams.ValidateChannelLayout(decodedFrame.ChannelLayout, decodedFrame.Channels);
                var needsDifferentSpec = FilterSpec.IsDifferentTo(decodedFrame) ||
                    FilterSpec.Layout != decoderChannelLayout ||
                    FilterSpec.SampleRate != decodedFrame.SampleRate ||
                    PacketGroupIndex != lastPacketGroupIndex;

                if (needsDifferentSpec)
                {
                    var decoderLayoutString = AudioParams.GetChannelLayoutString(decoderChannelLayout);

                    Helpers.LogDebug(
                       $"Audio frame changed from " +
                       $"rate:{FilterSpec.SampleRate} ch:{FilterSpec.Channels} fmt:{FilterSpec.SampleFormatName} layout:{FilterSpec.LayoutString} serial:{lastPacketGroupIndex} to " +
                       $"rate:{decodedFrame.SampleRate} ch:{decodedFrame.Channels} fmt:{AudioParams.GetSampleFormatName(decodedFrame.SampleFormat)} layout:{decoderLayoutString} serial:{PacketGroupIndex}\n");

                    FilterSpec.ImportFrom(decodedFrame);
                    lastPacketGroupIndex = PacketGroupIndex;
                    resultCode = ConfigureFilters(true);

                    if (resultCode < 0)
                        break;
                }

                if ((resultCode = EnqueueInputFilter(decodedFrame)) < 0)
                    break;

                var isFrameQueueAvailable = true;
                while ((resultCode = DequeueOutputFilter(decodedFrame)) >= 0)
                {
                    var queuedFrame = Frames.PeekWriteable();

                    if (queuedFrame == null)
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

}
