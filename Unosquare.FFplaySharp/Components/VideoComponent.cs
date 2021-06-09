namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg;
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Linq;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class VideoComponent : FilteringMediaComponent
    {
        private double FilterDelay;

        public VideoComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public int CurrentFilterIndex { get; set; }

        public int DroppedFrameCount { get; private set; }

        public SwsContext* ConvertContext;

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_VIDEO;

        public override string WantedCodecName => Container.Options.AudioForcedCodecName;

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.VideoFrameQueueCapacity, true);

        protected override void DecodingThreadMethod()
        {
            int resultCode;
            var frameRate = GuessFrameRate();

            AVFrame* decodedFrame;
            var lastWidth = 0;
            var lastHeight = 0;
            var lastFormat = -2;
            var lastGroupIndex = -1;
            var lastFilterIndex = 0;

            while (true)
            {
                resultCode = DecodeFrame(out decodedFrame);

                if (resultCode < 0)
                    break;

                if (resultCode == 0)
                    continue;

                var isReconfigNeeded = lastWidth != decodedFrame->width || lastHeight != decodedFrame->height || lastFormat != decodedFrame->format ||
                    lastGroupIndex != PacketGroupIndex || lastFilterIndex != CurrentFilterIndex;

                if (isReconfigNeeded)
                {
                    var lastFormatName = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)lastFormat) ?? "none";
                    var frameFormatName = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)decodedFrame->format) ?? "none";

                    Helpers.LogDebug(
                           $"Video frame changed from size:{lastWidth}x%{lastHeight} format:{lastFormatName} serial:{lastGroupIndex} to " +
                           $"size:{decodedFrame->width}x{decodedFrame->height} format:{frameFormatName} serial:{PacketGroupIndex}\n");

                    ReallocateFilterGraph();

                    var filterLiteral = Container.Options.VideoFilterGraphs.Count > 0
                        ? Container.Options.VideoFilterGraphs[CurrentFilterIndex]
                        : null;

                    if ((resultCode = ConfigureFilters(filterLiteral, decodedFrame)) < 0)
                    {
                        var evt = new SDL.SDL_Event() { type = (SDL.SDL_EventType)Constants.FF_QUIT_EVENT, };
                        // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                        SDL.SDL_PushEvent(ref evt);
                        break;
                    }

                    lastWidth = decodedFrame->width;
                    lastHeight = decodedFrame->height;
                    lastFormat = decodedFrame->format;
                    lastGroupIndex = PacketGroupIndex;
                    lastFilterIndex = CurrentFilterIndex;
                    frameRate = ffmpeg.av_buffersink_get_frame_rate(OutputFilter);
                }

                resultCode = EnqueueInputFilter(decodedFrame);
                if (resultCode < 0)
                    break;

                while (resultCode >= 0)
                {
                    var preFilteringTime = Clock.SystemTime;
                    resultCode = DequeueOutputFilter(decodedFrame);

                    if (resultCode < 0)
                    {
                        if (resultCode == ffmpeg.AVERROR_EOF)
                            FinalPacketGroupIndex = PacketGroupIndex;

                        resultCode = 0;
                        break;
                    }

                    FilterDelay = Clock.SystemTime - preFilteringTime;
                    if (Math.Abs(FilterDelay) > Constants.AV_NOSYNC_THRESHOLD / 10.0)
                        FilterDelay = 0;

                    var duration = (frameRate.num != 0 && frameRate.den != 0
                        ? ffmpeg.av_make_q(frameRate.den, frameRate.num).ToFactor()
                        : 0);

                    var frameTime = decodedFrame->pts.IsValidPts()
                        ? decodedFrame->pts * OutputFilterTimeBase.ToFactor()
                        : double.NaN;

                    resultCode = EnqueueFrame(decodedFrame, frameTime, duration, PacketGroupIndex);
                    ffmpeg.av_frame_unref(decodedFrame);

                    if (Packets.GroupIndex != PacketGroupIndex)
                        break;
                }

                if (resultCode < 0)
                    break;
            }

            ReleaseFilterGraph();
            ffmpeg.av_frame_free(&decodedFrame);
            return; // 0;
        }

        private AVRational GuessFrameRate() => ffmpeg.av_guess_frame_rate(Container.InputContext, Stream, null);

        private int EnqueueFrame(AVFrame* sourceFrame, double frameTime, double duration, int groupIndex)
        {
            var queuedFrame = Frames.PeekWriteable();

            if (queuedFrame == null)
                return -1;

            queuedFrame.Update(sourceFrame, groupIndex, frameTime, duration);
            Frames.Enqueue();

            Container.Renderer.Video.set_default_window_size(queuedFrame.Width, queuedFrame.Height, queuedFrame.Sar);
            return 0;
        }

        private int DecodeFrame(out AVFrame* frame)
        {
            frame = null;
            var gotPicture = DecodeFrame(out frame, out _);

            if (gotPicture < 0)
                return -1;

            if (gotPicture == 0)
                return 0;

            frame->sample_aspect_ratio = ffmpeg.av_guess_sample_aspect_ratio(Container.InputContext, Stream, frame);

            if (Container.Options.IsFrameDropEnabled > 0 || (Container.Options.IsFrameDropEnabled != 0 && Container.MasterSyncMode != ClockSync.Video))
            {
                if (frame->pts.IsValidPts())
                {
                    var frameTime = Stream->time_base.ToFactor() * frame->pts;
                    var frameDelay = frameTime - Container.MasterTime;

                    if (!frameDelay.IsNaN() && Math.Abs(frameDelay) < Constants.AV_NOSYNC_THRESHOLD &&
                        frameDelay - FilterDelay < 0 &&
                        PacketGroupIndex == Container.VideoClock.GroupIndex &&
                        Packets.Count != 0)
                    {
                        DroppedFrameCount++;
                        ffmpeg.av_frame_unref(frame);
                        gotPicture = 0;
                    }
                }
            }

            return gotPicture;
        }

        /// <summary>
        /// Port of the INSERT_FILT macro.
        /// this macro adds a filter before the lastly added filter, so the 
        /// processing order of the filters is in reverse
        /// </summary>
        /// <param name="filterName"></param>
        /// <param name="filterArgs"></param>
        /// <param name="resultCode"></param>
        /// <param name="lastFilterContext"></param>
        /// <returns></returns>
        private unsafe bool InsertFilter(
            string filterName, string filterArgs, ref int resultCode, ref AVFilterContext* lastFilterContext)
        {
            var insertedFilter = ffmpeg.avfilter_get_by_name(filterName);
            AVFilterContext* insertedFilterContext;

            resultCode = ffmpeg.avfilter_graph_create_filter(
                &insertedFilterContext, insertedFilter, $"ff_{filterName}", filterArgs, null, FilterGraph);

            if (resultCode < 0)
                return false;

            resultCode = ffmpeg.avfilter_link(insertedFilterContext, 0, lastFilterContext, 0);

            if (resultCode < 0)
                return false;

            lastFilterContext = insertedFilterContext;
            return true;
        }


        private int ConfigureFilters(string filterGraphLiteral, AVFrame* decoderFrame)
        {
            var resultCode = 0;
            AVFilterContext* sourceFilter = null;
            AVFilterContext* outputFilter = null;
            AVFilterContext* lastFilter = null;

            var codecParameters = Stream->codecpar;
            var frameRate = GuessFrameRate();
            var outputPixelFormats = Container.Renderer.Video.RetrieveSupportedPixelFormats().Cast<int>();
            var softwareScalerOptions = Dictionary.Extract(Container.Options.ScalerOptions);
            var softwareScalerFlags = string.Empty;

            foreach (var kvp in softwareScalerOptions)
            {
                softwareScalerFlags = (kvp.Key == "sws_flags")
                    ? $"flags={kvp.Value}:{softwareScalerFlags}"
                    : $"{kvp.Key}={kvp.Value}:{softwareScalerFlags}";
            }

            if (string.IsNullOrWhiteSpace(softwareScalerFlags))
                softwareScalerFlags = null;

            FilterGraph->scale_sws_opts = softwareScalerFlags != null
                ? ffmpeg.av_strdup(softwareScalerFlags)
                : null;

            var sourceFilterArguments =
                $"video_size={decoderFrame->width}x{decoderFrame->height}" +
                $":pix_fmt={decoderFrame->format}" +
                $":time_base={Stream->time_base.num}/{Stream->time_base.den}" +
                $":pixel_aspect={codecParameters->sample_aspect_ratio.num}/{Math.Max(codecParameters->sample_aspect_ratio.den, 1)}";

            if (frameRate.num != 0 && frameRate.den != 0)
                sourceFilterArguments = $"{sourceFilterArguments}:frame_rate={frameRate.num}/{frameRate.den}";

            const string SourceBufferName = "videoSourceBuffer";
            const string SinkBufferName = "videoSinkBuffer";

            var sourceBuffer = ffmpeg.avfilter_get_by_name("buffer");
            var sinkBuffer = ffmpeg.avfilter_get_by_name("buffersink");

            resultCode = ffmpeg.avfilter_graph_create_filter(
                &sourceFilter, sourceBuffer, SourceBufferName, sourceFilterArguments, null, FilterGraph);

            if (resultCode < 0)
                goto fail;

            resultCode = ffmpeg.avfilter_graph_create_filter(
                &outputFilter, sinkBuffer, SinkBufferName, null, null, FilterGraph);

            if (resultCode < 0)
                goto fail;

            resultCode = Helpers.av_opt_set_int_list(outputFilter, "pix_fmts", outputPixelFormats.ToArray(), ffmpeg.AV_OPT_SEARCH_CHILDREN);
            if (resultCode < 0)
                goto fail;

            lastFilter = outputFilter;
            if (Container.Options.IsAutorotateEnabled)
            {
                var theta = Helpers.ComputeDisplayRotation(Stream);

                if (Math.Abs(theta - 90) < 1.0)
                {
                    if (!InsertFilter("transpose", "clock", ref resultCode, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta - 180) < 1.0)
                {
                    if (!InsertFilter("hflip", null, ref resultCode, ref lastFilter))
                        goto fail;

                    if (!InsertFilter("vflip", null, ref resultCode, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta - 270) < 1.0)
                {
                    if (!InsertFilter("transpose", "cclock", ref resultCode, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta) > 1.0)
                {
                    if (!InsertFilter("rotate", $"{theta}*PI/180", ref resultCode, ref lastFilter))
                        goto fail;
                }
            }

            if ((resultCode = MaterializeFilterGraph(filterGraphLiteral, sourceFilter, lastFilter)) < 0)
                goto fail;

            InputFilter = sourceFilter;
            OutputFilter = outputFilter;

            fail:
            return resultCode;
        }
    }
}
