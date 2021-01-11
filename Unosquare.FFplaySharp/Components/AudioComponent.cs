namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class AudioComponent : FilteringMediaComponent
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

        public int FrameSerial { get; private set; } = -1;

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

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_AUDIO;

        public int ConfigureFilters(AVCodecContext* codecContext)
        {
            FilterSpec.ImportFrom(codecContext);
            return ConfigureFilters(false);
        }

        public int ConfigureFilters(bool forceOutputFormat)
        {
            const int SearhChildrenFlags = ffmpeg.AV_OPT_SEARCH_CHILDREN;

            var o = Container.Options;

            var sample_fmts = new[] { (int)AVSampleFormat.AV_SAMPLE_FMT_S16 };

            AVFilterContext* inputFilterContext = null;
            AVFilterContext* outputFilterContext = null;
            AVDictionaryEntry* entry = null;

            int ret;

            {
                var audioFilterGraph = FilterGraph;
                ffmpeg.avfilter_graph_free(&audioFilterGraph);
                FilterGraph = null;
            }

            FilterGraph = ffmpeg.avfilter_graph_alloc();
            FilterGraph->nb_threads = o.filter_nbthreads;

            string resamplerOptions = string.Empty;
            while ((entry = ffmpeg.av_dict_get(o.swr_opts, "", entry, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString(entry->key);
                var value = Helpers.PtrToString(entry->value);
                resamplerOptions = $"{key}={value}:{resamplerOptions}";
            }

            if (string.IsNullOrWhiteSpace(resamplerOptions))
                resamplerOptions = null;

            ffmpeg.av_opt_set(FilterGraph, "aresample_swr_opts", resamplerOptions, 0);
            var sourceBufferOptions = $"sample_rate={FilterSpec.Frequency}:sample_fmt={ffmpeg.av_get_sample_fmt_name(FilterSpec.SampleFormat)}:" +
                $"channels={FilterSpec.Channels}:time_base={1}/{FilterSpec.Frequency}";

            if (FilterSpec.Layout != 0)
                sourceBufferOptions = $"{sourceBufferOptions}:channel_layout=0x{FilterSpec.Layout:x16}";

            const string SourceBufferName = "audioSourceBuffer";
            const string SinkBufferName = "audioSinkBuffer";

            var sourceBuffer = ffmpeg.avfilter_get_by_name("abuffer");
            var sinkBuffer = ffmpeg.avfilter_get_by_name("abuffersink");

            ret = ffmpeg.avfilter_graph_create_filter(
                &inputFilterContext, sourceBuffer, SourceBufferName, sourceBufferOptions, null, FilterGraph);

            if (ret < 0)
                goto end;

            ret = ffmpeg.avfilter_graph_create_filter(
                &outputFilterContext, sinkBuffer, SinkBufferName, null, null, FilterGraph);

            if (ret < 0)
                goto end;

            if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "sample_fmts", sample_fmts, SearhChildrenFlags)) < 0)
                goto end;

            if ((ret = ffmpeg.av_opt_set_int(outputFilterContext, "all_channel_counts", 1, SearhChildrenFlags)) < 0)
                goto end;

            if (forceOutputFormat)
            {
                var outputChannelLayout = new[] { HardwareSpec.Layout };
                var outputChannelCount = new[] { HardwareSpec.Layout != 0 ? -1 : HardwareSpec.Channels };
                var outputSampleRates = new[] { HardwareSpec.Frequency };

                if ((ret = ffmpeg.av_opt_set_int(outputFilterContext, "all_channel_counts", 0, SearhChildrenFlags)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "channel_layouts", outputChannelLayout, SearhChildrenFlags)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "channel_counts", outputChannelCount, SearhChildrenFlags)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "sample_rates", outputSampleRates, SearhChildrenFlags)) < 0)
                    goto end;
            }

            var filterGraphLiteral = o.afilters;
            ret = MaterializeFilterGraph(FilterGraph, filterGraphLiteral, inputFilterContext, outputFilterContext);

            if (ret < 0) goto end;

            InputFilter = inputFilterContext;
            OutputFilter = outputFilterContext;

        end:
            if (ret < 0)
            {
                var filterGraph = FilterGraph;
                ffmpeg.avfilter_graph_free(&filterGraph);
                FilterGraph = null;
            }

            return ret;
        }

        /**
* Decode one audio frame and return its uncompressed size.
*
* The processed audio frame is decoded, converted if required, and
* stored in is->audio_buf, with size in bytes given by the return
* value.
*/
        public int RefillOutputBuffer()
        {
            FrameHolder af;

            if (Container.IsPaused)
                return -1;

            do
            {
                while (Frames.PendingCount == 0)
                {
                    if ((Clock.SystemTime - Container.Renderer.AudioCallbackTime) > HardwareBufferSize / HardwareSpec.BytesPerSecond / 2)
                        return -1;

                    Thread.Sleep(1);
                }

                if ((af = Frames.PeekReadable()) == null)
                    return -1;

                Frames.Next();

            } while (af.Serial != Packets.Serial);

            var frameBufferSize = ffmpeg.av_samples_get_buffer_size(null, af.Channels, af.SampleCount, af.SampleFormat, 1);
            var frameChannelLayout = AudioParams.ComputeChannelLayout(af.FramePtr);
            var wantedSampleCount = synchronize_audio(af.SampleCount);

            if (af.SampleFormat != StreamSpec.SampleFormat ||
                frameChannelLayout != StreamSpec.Layout ||
                af.Frequency != StreamSpec.Frequency ||
                (wantedSampleCount != af.SampleCount && ConvertContext == null))
            {
                var convertContext = ConvertContext;
                ffmpeg.swr_free(&convertContext);
                ConvertContext = null;

                ConvertContext = ffmpeg.swr_alloc_set_opts(null,
                    HardwareSpec.Layout, HardwareSpec.SampleFormat, HardwareSpec.Frequency,
                    frameChannelLayout, af.SampleFormat, af.Frequency,
                    0, null);

                if (ConvertContext == null || ffmpeg.swr_init(ConvertContext) < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"Cannot create sample rate converter for conversion of {af.Frequency} Hz " +
                           $"{ffmpeg.av_get_sample_fmt_name(af.SampleFormat)} {af.Channels} channels to " +
                           $"{HardwareSpec.Frequency} Hz {ffmpeg.av_get_sample_fmt_name(HardwareSpec.SampleFormat)} {HardwareSpec.Channels} channels!\n");

                    convertContext = ConvertContext;
                    ffmpeg.swr_free(&convertContext);
                    ConvertContext = null;

                    return -1;
                }

                StreamSpec.ImportFrom(af.FramePtr);
                StreamSpec.Layout = frameChannelLayout;
            }

            var resampledBufferSize = 0;

            if (ConvertContext != null)
            {
                var wantedOutputSize = wantedSampleCount * HardwareSpec.Frequency / af.Frequency + 256;
                var outputBufferSize = ffmpeg.av_samples_get_buffer_size(null, HardwareSpec.Channels, wantedOutputSize, HardwareSpec.SampleFormat, 0);

                if (outputBufferSize < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size() failed\n");
                    return -1;
                }
                if (wantedSampleCount != af.SampleCount)
                {
                    if (ffmpeg.swr_set_compensation(ConvertContext, (wantedSampleCount - af.SampleCount) * HardwareSpec.Frequency / af.Frequency,
                                                wantedSampleCount * HardwareSpec.Frequency / af.Frequency) < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_set_compensation() failed\n");
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

                var audioBufferIn = af.FramePtr->extended_data;
                var audioBufferOut = ResampledOutputBuffer;
                var outputSampleCount = ffmpeg.swr_convert(ConvertContext, &audioBufferOut, wantedOutputSize, audioBufferIn, af.SampleCount);
                ResampledOutputBuffer = audioBufferOut;
                af.FramePtr->extended_data = audioBufferIn;

                if (outputSampleCount < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_convert() failed\n");
                    return -1;
                }
                if (outputSampleCount == wantedOutputSize)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, "audio buffer is probably too small\n");
                    if (ffmpeg.swr_init(ConvertContext) < 0)
                    {
                        var convertContext = ConvertContext;
                        ffmpeg.swr_free(&convertContext);
                        ConvertContext = convertContext;
                    }
                }

                OutputBuffer = ResampledOutputBuffer;
                resampledBufferSize = outputSampleCount * HardwareSpec.Channels * HardwareSpec.BytesPerSample;
            }
            else
            {
                OutputBuffer = af.FramePtr->data[0];
                resampledBufferSize = frameBufferSize;
            }

            var currentFrameTime = FrameTime;

            // update the audio clock with the pts
            FrameTime = af.HasValidTime ? af.Time + (double)af.SampleCount / af.Frequency : double.NaN;

            FrameSerial = af.Serial;
            if (Debugger.IsAttached)
            {
                Console.WriteLine($"audio: delay={(FrameTime - LastFrameTime),-8:0.####} clock={FrameTime,-8:0.####} clock0={currentFrameTime,-8:0.####}");
                LastFrameTime = FrameTime;
            }

            return resampledBufferSize;
        }

        /* return the wanted number of samples to get better sync if sync_type is video
* or external master clock */
        public int synchronize_audio(int sampleCount)
        {
            var wantedSampleCount = sampleCount;

            /* if not master, then we try to remove or add samples to correct the clock */
            if (Container.MasterSyncMode == ClockSync.Audio)
                return wantedSampleCount;

            var clockDelay = Container.AudioClock.Value - Container.MasterTime;

            if (!clockDelay.IsNaN() && Math.Abs(clockDelay) < Constants.AV_NOSYNC_THRESHOLD)
            {
                SyncDiffTotalDelay = clockDelay + (SyncDiffAverageCoffiecient * SyncDiffTotalDelay);
                if (SyncDiffAverageCount < Constants.AUDIO_DIFF_AVG_NB)
                {
                    /* not enough measures to have a correct estimate */
                    SyncDiffAverageCount++;
                }
                else
                {
                    /* estimate the A-V difference */
                    var syncDiffDelay = SyncDiffTotalDelay * (1.0 - SyncDiffAverageCoffiecient);

                    if (Math.Abs(syncDiffDelay) >= SyncDiffDelayThreshold)
                    {
                        wantedSampleCount = sampleCount + (int)(clockDelay * StreamSpec.Frequency);
                        var minSampleCount = (int)(sampleCount * (100 - Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100);
                        var maxSampleCount = (int)(sampleCount * (100 + Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100);
                        wantedSampleCount = wantedSampleCount.Clamp(minSampleCount, maxSampleCount);
                    }

                    ffmpeg.av_log(
                        null, ffmpeg.AV_LOG_TRACE, $"diff={clockDelay} adiff={syncDiffDelay} sample_diff={(wantedSampleCount - sampleCount)} apts={FrameTime} {SyncDiffDelayThreshold}\n");
                }
            }
            else
            {
                /* too big difference : may be initial PTS errors, so
                   reset A-V filter */
                SyncDiffAverageCount = 0;
                SyncDiffTotalDelay = 0;
            }

            return wantedSampleCount;
        }

        public override void Close()
        {
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext->nb_streams)
                return;

            AbortDecoder();
            Container.Renderer.CloseAudio();
            DisposeDecoder();

            var contextPointer = ConvertContext;
            ffmpeg.swr_free(&contextPointer);
            ConvertContext = null;

            if (ResampledOutputBuffer != null)
                ffmpeg.av_free(ResampledOutputBuffer);

            ResampledOutputBuffer = null;
            ResampledOutputBufferSize = 0;
            OutputBuffer = null;

            Container.InputContext->streams[StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
            Stream = null;
            StreamIndex = -1;
        }

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.AudioFrameQueueCapacity, true);

        public override unsafe int InitializeDecoder(AVCodecContext* codecContext, int streamIndex)
        {
            FilterSpec.ImportFrom(codecContext);
            if (ConfigureFilters(false) < 0)
                return -1;

            var wantedSpec = AudioParams.FromFilterContext(OutputFilter);
            var hardwareBufferSize = Container.Renderer.audio_open(wantedSpec, out var audioHardwareSpec);
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
                StartPts = Stream->start_time;
                StartPtsTimeBase = Stream->time_base;
            }
            else
            {
                StartPts = ffmpeg.AV_NOPTS_VALUE;
                StartPtsTimeBase = ffmpeg.av_make_q(0, 0);
            }

            return 0;
        }

        private int DecodeFrame(out AVFrame* frame)
        {
            var resultCode = DecodeFrame(out frame, out _);
            if (resultCode >= 0)
            {
                var decoderTimeBase = ffmpeg.av_make_q(1, frame->sample_rate);

                if (frame->pts.IsValidPts())
                    frame->pts = ffmpeg.av_rescale_q(frame->pts, CodecContext->pkt_timebase, decoderTimeBase);
                else if (NextPts.IsValidPts())
                    frame->pts = ffmpeg.av_rescale_q(NextPts, NextPtsTimeBase, decoderTimeBase);

                if (frame->pts.IsValidPts())
                {
                    NextPts = frame->pts + frame->nb_samples;
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
            var lastPacketSerial = -1;
            var gotSamples = 0;
            var resultCode = 0;

            AVFrame* decodedFrame;

            const int StringBufferLength = 1024;
            var filterLayoutString = stackalloc byte[StringBufferLength];
            var decoderLayoutString = stackalloc byte[StringBufferLength];

            do
            {
                if ((gotSamples = DecodeFrame(out decodedFrame)) < 0)
                    goto the_end;

                if (gotSamples != 0)
                {
                    var decoderTimeBase = ffmpeg.av_make_q(1, decodedFrame->sample_rate);
                    var decoderChannelLayout = AudioParams.ValidateChannelLayout(decodedFrame->channel_layout, decodedFrame->channels);

                    var reconfigure = FilterSpec.IsDifferent(decodedFrame) ||
                        FilterSpec.Layout != decoderChannelLayout ||
                        FilterSpec.Frequency != decodedFrame->sample_rate ||
                        PacketSerial != lastPacketSerial;

                    if (reconfigure)
                    {
                        ffmpeg.av_get_channel_layout_string(filterLayoutString, StringBufferLength, -1, (ulong)FilterSpec.Layout);
                        ffmpeg.av_get_channel_layout_string(decoderLayoutString, StringBufferLength, -1, (ulong)decoderChannelLayout);
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Audio frame changed from " +
                           $"rate:{FilterSpec.Frequency} ch:{FilterSpec.Channels} fmt:{ffmpeg.av_get_sample_fmt_name(FilterSpec.SampleFormat)} layout:{Helpers.PtrToString(filterLayoutString)} serial:{lastPacketSerial} to " +
                           $"rate:{decodedFrame->sample_rate} ch:{decodedFrame->channels} fmt:{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)decodedFrame->format)} layout:{Helpers.PtrToString(decoderLayoutString)} serial:{PacketSerial}\n");

                        FilterSpec.ImportFrom(decodedFrame);
                        lastPacketSerial = PacketSerial;

                        if ((resultCode = ConfigureFilters(true)) < 0)
                            goto the_end;
                    }

                    if ((resultCode = ffmpeg.av_buffersrc_add_frame(InputFilter, decodedFrame)) < 0)
                        goto the_end;

                    while ((resultCode = ffmpeg.av_buffersink_get_frame_flags(OutputFilter, decodedFrame, 0)) >= 0)
                    {
                        decoderTimeBase = ffmpeg.av_buffersink_get_time_base(OutputFilter);
                        var queuedFrame = Frames.PeekWriteable();

                        if (queuedFrame == null)
                            goto the_end;

                        queuedFrame.Time = decodedFrame->pts.IsValidPts()
                            ? decodedFrame->pts * ffmpeg.av_q2d(decoderTimeBase)
                            : double.NaN;

                        queuedFrame.Position = decodedFrame->pkt_pos;
                        queuedFrame.Serial = PacketSerial;
                        queuedFrame.Duration = ffmpeg.av_q2d(ffmpeg.av_make_q(decodedFrame->nb_samples, decodedFrame->sample_rate));

                        ffmpeg.av_frame_move_ref(queuedFrame.FramePtr, decodedFrame);
                        Frames.Push();

                        if (Packets.Serial != PacketSerial)
                            break;
                    }

                    if (resultCode == ffmpeg.AVERROR_EOF)
                        EndOfFileSerial = PacketSerial;
                }
            } while (resultCode >= 0 || resultCode == ffmpeg.AVERROR(ffmpeg.EAGAIN) || resultCode == ffmpeg.AVERROR_EOF);

        the_end:
            var filterGraph = FilterGraph;
            ffmpeg.avfilter_graph_free(&filterGraph);
            FilterGraph = null;
            ffmpeg.av_frame_free(&decodedFrame);
        }
    }

}
