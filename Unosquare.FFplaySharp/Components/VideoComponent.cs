namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
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
            var frameRate = ffmpeg.av_guess_frame_rate(Container.InputContext, Stream, null);

            AVFrame* decodedFrame;
            AVFilterGraph* filterGraph = null;
            AVFilterContext* outputFilter = null;
            AVFilterContext* inputFilter = null;

            var lastWidth = 0;
            var lastHeight = 0;
            var lastFormat = -2;
            var lastSerial = -1;
            var lastFilterIndex = 0;

            while (true)
            {
                resultCode = DecodeFrame(out decodedFrame);

                if (resultCode < 0)
                    goto the_end;

                if (resultCode == 0)
                    continue;

                var isReconfigNeeded = lastWidth != decodedFrame->width || lastHeight != decodedFrame->height || lastFormat != decodedFrame->format ||
                    lastSerial != PacketSerial || lastFilterIndex != CurrentFilterIndex;

                if (isReconfigNeeded)
                {
                    var lastFormatName = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)lastFormat) ?? "none";
                    var frameFormatName = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)decodedFrame->format) ?? "none";

                    ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Video frame changed from size:{lastWidth}x%{lastHeight} format:{lastFormatName} serial:{lastSerial} to " +
                           $"size:{decodedFrame->width}x{decodedFrame->height} format:{frameFormatName} serial:{PacketSerial}\n");

                    ffmpeg.avfilter_graph_free(&filterGraph);
                    filterGraph = ffmpeg.avfilter_graph_alloc();
                    filterGraph->nb_threads = Container.Options.filter_nbthreads;

                    var filterLiteral = Container.Options.vfilters_list.Count > 0
                        ? Container.Options.vfilters_list[CurrentFilterIndex]
                        : null;

                    if ((resultCode = ConfigureFilters(filterGraph, filterLiteral, decodedFrame)) < 0)
                    {
                        var evt = new SDL.SDL_Event() { type = (SDL.SDL_EventType)Constants.FF_QUIT_EVENT, };
                        // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                        SDL.SDL_PushEvent(ref evt);
                        goto the_end;
                    }

                    inputFilter = InputFilter;
                    outputFilter = OutputFilter;
                    lastWidth = decodedFrame->width;
                    lastHeight = decodedFrame->height;
                    lastFormat = decodedFrame->format;
                    lastSerial = PacketSerial;
                    lastFilterIndex = CurrentFilterIndex;
                    frameRate = ffmpeg.av_buffersink_get_frame_rate(outputFilter);
                }

                resultCode = ffmpeg.av_buffersrc_add_frame(inputFilter, decodedFrame);
                if (resultCode < 0)
                    goto the_end;

                while (resultCode >= 0)
                {
                    var preFilteringTime = Clock.SystemTime;

                    resultCode = ffmpeg.av_buffersink_get_frame_flags(outputFilter, decodedFrame, 0);
                    if (resultCode < 0)
                    {
                        if (resultCode == ffmpeg.AVERROR_EOF)
                            EndOfFileSerial = PacketSerial;

                        resultCode = 0;
                        break;
                    }

                    FilterDelay = Clock.SystemTime - preFilteringTime;
                    if (Math.Abs(FilterDelay) > Constants.AV_NOSYNC_THRESHOLD / 10.0)
                        FilterDelay = 0;

                    var duration = (frameRate.num != 0 && frameRate.den != 0
                        ? ffmpeg.av_q2d(new AVRational() { num = frameRate.den, den = frameRate.num })
                        : 0);

                    var tb = ffmpeg.av_buffersink_get_time_base(outputFilter);
                    var pts = decodedFrame->pts.IsValidPts()
                        ? decodedFrame->pts * ffmpeg.av_q2d(tb)
                        : double.NaN;

                    resultCode = EnqueueFrame(decodedFrame, pts, duration, PacketSerial);
                    ffmpeg.av_frame_unref(decodedFrame);

                    if (Packets.Serial != PacketSerial)
                        break;
                }

                if (resultCode < 0)
                    goto the_end;
            }
        the_end:

            ffmpeg.avfilter_graph_free(&filterGraph);
            ffmpeg.av_frame_free(&decodedFrame);
            return; // 0;
        }

        private int EnqueueFrame(AVFrame* sourceFrame, double pts, double duration, int serial)
        {
            var frameSlot = Frames.PeekWriteable();

            if (frameSlot == null)
                return -1;

            frameSlot.Sar = sourceFrame->sample_aspect_ratio;
            frameSlot.uploaded = false;

            frameSlot.Width = sourceFrame->width;
            frameSlot.Height = sourceFrame->height;
            frameSlot.Format = sourceFrame->format;

            frameSlot.Time = pts;
            frameSlot.Duration = duration;
            frameSlot.Position = sourceFrame->pkt_pos;
            frameSlot.Serial = serial;

            Container.Renderer.set_default_window_size(frameSlot.Width, frameSlot.Height, frameSlot.Sar);

            ffmpeg.av_frame_move_ref(frameSlot.FramePtr, sourceFrame);
            Frames.Push();
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

            if (Container.Options.framedrop > 0 || (Container.Options.framedrop != 0 && Container.MasterSyncMode != ClockSync.Video))
            {
                if (frame->pts.IsValidPts())
                {
                    var frameTime = ffmpeg.av_q2d(Stream->time_base) * frame->pts;
                    var frameDelay = frameTime - Container.MasterTime;

                    if (!frameDelay.IsNaN() && Math.Abs(frameDelay) < Constants.AV_NOSYNC_THRESHOLD &&
                        frameDelay - FilterDelay < 0 &&
                        PacketSerial == Container.VideoClock.Serial &&
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

        private static unsafe bool InsertFilter(
            string filterName, string filterArgs, AVFilterGraph* filterGraph, ref int ret, ref AVFilterContext* lastFilterContext)
        {
            do
            {
                var insertedFilter = ffmpeg.avfilter_get_by_name(filterName);
                AVFilterContext* insertedFilterContext;

                ret = ffmpeg.avfilter_graph_create_filter(
                    &insertedFilterContext, insertedFilter, $"ff_{filterName}", filterArgs, null, filterGraph);

                if (ret < 0)
                    return false;

                ret = ffmpeg.avfilter_link(insertedFilterContext, 0, lastFilterContext, 0);

                if (ret < 0)
                    return false;

                lastFilterContext = insertedFilterContext;
            } while (false);

            return true;
        }


        private int ConfigureFilters(AVFilterGraph* filterGraph, string filterGraphLiteral, AVFrame* decoderFrame)
        {
            int ret;
            AVFilterContext* sourceFilter = null;
            AVFilterContext* outputFilter = null;
            AVFilterContext* lastFilter = null;

            var codecParameters = Stream->codecpar;
            var frameRate = ffmpeg.av_guess_frame_rate(Container.InputContext, Stream, null);

            var outputPixelFormats = new List<int>(MediaRenderer.sdl_texture_map.Count);
            for (var i = 0; i < Container.Renderer.SdlRendererInfo.num_texture_formats; i++)
            {
                foreach (var kvp in MediaRenderer.sdl_texture_map)
                {
                    if (kvp.Value == Container.Renderer.SdlRendererInfo.texture_formats[i])
                        outputPixelFormats.Add((int)kvp.Key);
                }
            }

            var softwareScalerFlags = string.Empty;
            AVDictionaryEntry* optionEntry = null;
            while ((optionEntry = ffmpeg.av_dict_get(Container.Options.sws_dict, "", optionEntry, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString(optionEntry->key);
                var value = Helpers.PtrToString(optionEntry->value);

                softwareScalerFlags = (key == "sws_flags")
                    ? $"flags={value}:{softwareScalerFlags}"
                    : $"{key}={value}:{softwareScalerFlags}";
            }

            if (string.IsNullOrWhiteSpace(softwareScalerFlags))
                softwareScalerFlags = null;

            filterGraph->scale_sws_opts = softwareScalerFlags != null
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

            ret = ffmpeg.avfilter_graph_create_filter(
                &sourceFilter, sourceBuffer, SourceBufferName, sourceFilterArguments, null, filterGraph);

            if (ret < 0)
                goto fail;

            ret = ffmpeg.avfilter_graph_create_filter(
                &outputFilter, sinkBuffer, SinkBufferName, null, null, filterGraph);

            if (ret < 0)
                goto fail;

            ret = Helpers.av_opt_set_int_list(outputFilter, "pix_fmts", outputPixelFormats.ToArray(), ffmpeg.AV_OPT_SEARCH_CHILDREN);
            if (ret < 0)
                goto fail;

            lastFilter = outputFilter;
            if (Container.Options.autorotate)
            {
                var theta = Helpers.get_rotation(Stream);

                if (Math.Abs(theta - 90) < 1.0)
                {
                    if (!InsertFilter("transpose", "clock", filterGraph, ref ret, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta - 180) < 1.0)
                {
                    if (!InsertFilter("hflip", null, filterGraph, ref ret, ref lastFilter))
                        goto fail;

                    if (!InsertFilter("vflip", null, filterGraph, ref ret, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta - 270) < 1.0)
                {
                    if (!InsertFilter("transpose", "cclock", filterGraph, ref ret, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta) > 1.0)
                {
                    if (!InsertFilter("rotate", $"{theta}*PI/180", filterGraph, ref ret, ref lastFilter))
                        goto fail;
                }
            }

            if ((ret = MaterializeFilterGraph(filterGraph, filterGraphLiteral, sourceFilter, lastFilter)) < 0)
                goto fail;

            InputFilter = sourceFilter;
            OutputFilter = outputFilter;

        fail:
            return ret;
        }
    }
}
