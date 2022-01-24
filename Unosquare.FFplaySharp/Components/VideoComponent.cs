namespace Unosquare.FFplaySharp.Components;

public sealed class VideoComponent : FilteringMediaComponent
{
    private double FilterDelay;

    public VideoComponent(MediaContainer container)
        : base(container)
    {
        // placeholder
    }

    public int CurrentFilterIndex { get; set; }

    public int DroppedFrameCount { get; private set; }

    public RescalerContext ConvertContext { get; } = new();

    public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_VIDEO;

    public override string? WantedCodecName => Container.Options.AudioForcedCodecName;

    protected override FrameStore CreateFrameQueue() => new(Packets, Constants.VideoFrameQueueCapacity, true);

    protected override void DecodingThreadMethod()
    {
        int resultCode;
        var frameRate = Container.Input.GuessFrameRate(Stream);

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

                ($"Video frame changed from size:{lastWidth}x%{lastHeight} format:{lastFormatName} serial:{lastGroupIndex} to " +
                $"size:{decodedFrame.Width}x{decodedFrame.Height} format:{frameFormatName} serial:{PacketGroupIndex}.")
                .LogDebug();

                ReallocateFilterGraph();

                var filterLiteral = Container.Options.VideoFilterGraphs.Count > 0
                    ? Container.Options.VideoFilterGraphs[CurrentFilterIndex]
                    : null;

                try
                {
                    ConfigureFilters(filterLiteral, decodedFrame);
                }
                catch (Exception ex)
                {
                    Container.Presenter.HandleFatalException(ex);
                    break;
                }

                lastWidth = decodedFrame.Width;
                lastHeight = decodedFrame.Height;
                lastFormat = (int)decodedFrame.PixelFormat;
                lastGroupIndex = PacketGroupIndex;
                lastFilterIndex = CurrentFilterIndex;
                frameRate = OutputFilter.FrameRate;
            }

            resultCode = EnqueueFilteringFrame(decodedFrame);
            if (resultCode < 0)
                break;

            while (resultCode >= 0)
            {
                var preFilteringTime = Clock.SystemTime;
                resultCode = DequeueFilteringFrame(decodedFrame);

                if (resultCode < 0)
                {
                    if (resultCode == ffmpeg.AVERROR_EOF)
                        FinalPacketGroupIndex = PacketGroupIndex;

                    resultCode = 0;
                    break;
                }

                FilterDelay = Clock.SystemTime - preFilteringTime;
                if (Math.Abs(FilterDelay) > Constants.MediaNoSyncThreshold / 10.0)
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
        decodedFrame?.Release();
        return; // 0;
    }

    private int EnqueueFrame(FFFrame sourceFrame, double frameTime, double duration, int groupIndex)
    {
        if (!Frames.LeaseFrameForWriting(out var targetFrame))
            return -1;

        targetFrame.Update(sourceFrame, groupIndex, frameTime, duration);
        Frames.EnqueueLeasedFrame();

        Container.Presenter.UpdatePictureSize(
            targetFrame.Width, targetFrame.Height, targetFrame.Frame.SampleAspectRatio);

        return 0;
    }

    private int DecodeFrame(FFFrame frame)
    {
        var gotPicture = DecodeFrame(frame, null);

        if (gotPicture < 0)
            return -1;

        if (gotPicture == 0)
            return 0;

        frame.SampleAspectRatio = Container.Input.GuessAspectRatio(Stream, frame);

        if (Container.Options.IsFrameDropEnabled > 0 || (Container.Options.IsFrameDropEnabled != 0 && Container.MasterSyncMode != ClockSource.Video))
        {
            if (frame.Pts.IsValidPts())
            {
                var frameTime = Stream.TimeBase.ToFactor() * frame.Pts;
                var frameDelay = frameTime - Container.MasterTime;

                if (!frameDelay.IsNaN() && Math.Abs(frameDelay) < Constants.MediaNoSyncThreshold &&
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
    /// <param name="lastFilter"></param>
    /// <returns></returns>
    private FFFilterContext InsertFilter(
        string filterName, string? filterArgs, FFFilterContext lastFilter)
    {
        var filter = FFFilter.FromName(filterName);
        if (filter.IsNull())
            throw new ArgumentException($"Known filter name '{filterName}' not found.", nameof(filterName));

        var insertedFilter = FFFilterContext.Create(
            FilterGraph, filter!, $"ff_{filterName}", filterArgs);

        FFFilterContext.Link(insertedFilter, lastFilter);

        return insertedFilter;
    }

    private void ConfigureFilters(string? filterGraphLiteral, FFFrame decoderFrame)
    {
        var codecParameters = Stream.CodecParameters;
        var frameRate = Container.Input.GuessFrameRate(Stream);
        var outputPixelFormats = Container.Presenter.PixelFormats.Cast<int>();
        var softwareScalerFlags = string.Empty;

        foreach (var kvp in Container.Options.ScalerOptions)
        {
            softwareScalerFlags = (kvp.Key == "sws_flags")
                ? $"flags={kvp.Value}:{softwareScalerFlags}"
                : $"{kvp.Key}={kvp.Value}:{softwareScalerFlags}";
        }

        FilterGraph.SoftwareScalerOptions = string.IsNullOrWhiteSpace(softwareScalerFlags)
            ? default
            : softwareScalerFlags;

        var sourceFilterArguments =
            $"video_size={decoderFrame.Width}x{decoderFrame.Height}" +
            $":pix_fmt={(int)decoderFrame.PixelFormat}" +
            $":time_base={Stream.TimeBase.num}/{Stream.TimeBase.den}" +
            $":pixel_aspect={codecParameters.SampleAspectRatio.num}/{Math.Max(codecParameters.SampleAspectRatio.den, 1)}";

        if (frameRate.num != 0 && frameRate.den != 0)
            sourceFilterArguments = $"{sourceFilterArguments}:frame_rate={frameRate.num}/{frameRate.den}";

        var sourceFilter = FFFilterContext.Create(FilterGraph, "buffer", "videoSourceBuffer", sourceFilterArguments);
        var outputFilter = FFFilterContext.Create(FilterGraph, "buffersink", "videoSinkBuffer");

        outputFilter.SetOptionList("pix_fmts", outputPixelFormats.ToArray());

        var lastFilter = outputFilter;
        if (Container.Options.IsAutorotateEnabled)
        {
            var theta = Stream.ComputeDisplayRotation();

            if (Math.Abs(theta - 90) < 1.0)
            {
                lastFilter = InsertFilter("transpose", "clock", lastFilter);
            }
            else if (Math.Abs(theta - 180) < 1.0)
            {
                lastFilter = InsertFilter("hflip", null, lastFilter);
                lastFilter = InsertFilter("vflip", null, lastFilter);
            }
            else if (Math.Abs(theta - 270) < 1.0)
            {
                lastFilter = InsertFilter("transpose", "cclock", lastFilter);
            }
            else if (Math.Abs(theta) > 1.0)
            {
                lastFilter = InsertFilter("rotate", $"{theta}*PI/180", lastFilter);
            }
        }

        MaterializeFilterGraph(filterGraphLiteral, sourceFilter, lastFilter);
        InputFilter = sourceFilter;
        OutputFilter = outputFilter;
    }
}
