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
            var frameRate = Container.InputContext.GuessFrameRate(Stream);

            var decodedFrame = new FFFrame();
            var lastWidth = 0;
            var lastHeight = 0;
            var lastFormat = -2;
            var lastGroupIndex = -1;
            var lastFilterIndex = 0;

            while (true)
            {
                resultCode = DecodeFrame(decodedFrame);

                if (resultCode < 0)
                    break;

                if (resultCode == 0)
                    continue;

                var isReconfigNeeded = lastWidth != decodedFrame.Width || lastHeight != decodedFrame.Height || lastFormat != (int)decodedFrame.PixelFormat ||
                    lastGroupIndex != PacketGroupIndex || lastFilterIndex != CurrentFilterIndex;

                if (isReconfigNeeded)
                {
                    var lastFormatName = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)lastFormat) ?? "none";
                    var frameFormatName = ffmpeg.av_get_pix_fmt_name(decodedFrame.PixelFormat) ?? "none";

                    Helpers.LogDebug(
                           $"Video frame changed from size:{lastWidth}x%{lastHeight} format:{lastFormatName} serial:{lastGroupIndex} to " +
                           $"size:{decodedFrame.Width}x{decodedFrame.Height} format:{frameFormatName} serial:{PacketGroupIndex}\n");

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

                    lastWidth = decodedFrame.Width;
                    lastHeight = decodedFrame.Height;
                    lastFormat = (int)decodedFrame.PixelFormat;
                    lastGroupIndex = PacketGroupIndex;
                    lastFilterIndex = CurrentFilterIndex;
                    frameRate = OutputFilter.FrameRate;
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

                    var frameTime = decodedFrame.Pts.IsValidPts()
                        ? decodedFrame.Pts * OutputFilterTimeBase.ToFactor()
                        : double.NaN;

                    resultCode = EnqueueFrame(decodedFrame, frameTime, duration, PacketGroupIndex);
                    decodedFrame.Reset();

                    if (Packets.GroupIndex != PacketGroupIndex)
                        break;
                }

                if (resultCode < 0)
                    break;
            }

            ReleaseFilterGraph();
            decodedFrame.Release();
            return; // 0;
        }

        private int EnqueueFrame(FFFrame sourceFrame, double frameTime, double duration, int groupIndex)
        {
            var queuedFrame = Frames.PeekWriteable();

            if (queuedFrame == null)
                return -1;

            queuedFrame.Update(sourceFrame, groupIndex, frameTime, duration);
            Frames.Enqueue();

            Container.Renderer.Video.set_default_window_size(
                queuedFrame.Width, queuedFrame.Height, queuedFrame.Frame.SampleAspectRatio);

            return 0;
        }

        private int DecodeFrame(FFFrame frame)
        {
            var gotPicture = DecodeFrame(frame, null);

            if (gotPicture < 0)
                return -1;

            if (gotPicture == 0)
                return 0;

            frame.SampleAspectRatio = Container.InputContext.GuessAspectRatio(Stream, frame);

            if (Container.Options.IsFrameDropEnabled > 0 || (Container.Options.IsFrameDropEnabled != 0 && Container.MasterSyncMode != ClockSync.Video))
            {
                if (frame.Pts.IsValidPts())
                {
                    var frameTime = Stream.TimeBase.ToFactor() * frame.Pts;
                    var frameDelay = frameTime - Container.MasterTime;

                    if (!frameDelay.IsNaN() && Math.Abs(frameDelay) < Constants.AV_NOSYNC_THRESHOLD &&
                        frameDelay - FilterDelay < 0 &&
                        PacketGroupIndex == Container.VideoClock.GroupIndex &&
                        Packets.Count != 0)
                    {
                        DroppedFrameCount++;
                        frame.Reset();
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
            string filterName, string filterArgs, ref int resultCode, ref FFFilterContext lastFilterContext)
        {
            var insertedFilter = FFFilter.FromName(filterName);

            FFFilterContext insertedFilterContext;
            (insertedFilterContext, resultCode) = FFFilterContext.Create(
                FilterGraph, FFFilter.FromName(filterName), $"ff_{filterName}", filterArgs);

            if (resultCode < 0)
                return false;

            resultCode = FFFilterContext.Link(insertedFilterContext, lastFilterContext);

            if (resultCode < 0)
                return false;

            lastFilterContext = insertedFilterContext;
            return true;
        }


        private int ConfigureFilters(string filterGraphLiteral, FFFrame decoderFrame)
        {
            var resultCode = 0;
            
            var codecParameters = Stream.CodecParameters;
            var frameRate = Container.InputContext.GuessFrameRate(Stream);
            var outputPixelFormats = Container.Renderer.Video.RetrieveSupportedPixelFormats().Cast<int>();
            var softwareScalerFlags = string.Empty;

            foreach (var kvp in Container.Options.ScalerOptions)
            {
                softwareScalerFlags = (kvp.Key == "sws_flags")
                    ? $"flags={kvp.Value}:{softwareScalerFlags}"
                    : $"{kvp.Key}={kvp.Value}:{softwareScalerFlags}";
            }

            if (string.IsNullOrWhiteSpace(softwareScalerFlags))
                softwareScalerFlags = null;

            FilterGraph.SoftwareScalerOptions = softwareScalerFlags;

            var sourceFilterArguments =
                $"video_size={decoderFrame.Width}x{decoderFrame.Height}" +
                $":pix_fmt={(int)decoderFrame.PixelFormat}" +
                $":time_base={Stream.TimeBase.num}/{Stream.TimeBase.den}" +
                $":pixel_aspect={codecParameters.SampleAspectRatio.num}/{Math.Max(codecParameters.SampleAspectRatio.den, 1)}";

            if (frameRate.num != 0 && frameRate.den != 0)
                sourceFilterArguments = $"{sourceFilterArguments}:frame_rate={frameRate.num}/{frameRate.den}";


            const string SinkBufferName = "videoSinkBuffer";

            var sinkBuffer = FFFilter.FromName("buffersink");

            FFFilterContext sourceFilter = null;
            (sourceFilter, resultCode) = FFFilterContext.Create(
                FilterGraph, FFFilter.FromName("buffer"), "videoSourceBuffer", sourceFilterArguments);

            if (resultCode < 0)
                goto fail;

            FFFilterContext outputFilter;
            (outputFilter, resultCode) = FFFilterContext.Create(
                FilterGraph, sinkBuffer, SinkBufferName, null);

            if (resultCode < 0)
                goto fail;

            resultCode = outputFilter.SetOptionList("pix_fmts", outputPixelFormats.ToArray());
            if (resultCode < 0)
                goto fail;

            var lastFilter = outputFilter;
            if (Container.Options.IsAutorotateEnabled)
            {
                var theta = Stream.ComputeDisplayRotation();

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
