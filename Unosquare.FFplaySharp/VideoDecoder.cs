namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;

    public unsafe sealed class VideoDecoder : MediaDecoder
    {
        public VideoDecoder(MediaContainer container, AVCodecContext* codecContext)
            : base(container.Video, codecContext)
        {
            Component = container.Video;
        }

        public new VideoComponent Component { get; }

        public override void Start() => Start(VideoWorkerThreadMethod, nameof(VideoDecoder));

        private void VideoWorkerThreadMethod()
        {
            int ret;
            var frameRate = ffmpeg.av_guess_frame_rate(Container.InputContext, Component.Stream, null);

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
                ret = DecodeFrame(out decodedFrame);

                if (ret < 0)
                    goto the_end;

                if (ret == 0)
                    continue;

                var isReconfigNeeded = lastWidth != decodedFrame->width || lastHeight != decodedFrame->height || lastFormat != decodedFrame->format ||
                    lastSerial != PacketSerial || lastFilterIndex != Container.vfilter_idx;

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
                        ? Container.Options.vfilters_list[Container.vfilter_idx]
                        : null;

                    if ((ret = ConfigureFilters(ref filterGraph, filterLiteral, decodedFrame)) < 0)
                    {
                        var evt = new SDL.SDL_Event() { type = (SDL.SDL_EventType)Constants.FF_QUIT_EVENT, };
                        // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                        SDL.SDL_PushEvent(ref evt);
                        goto the_end;
                    }

                    inputFilter = Component.InputFilter;
                    outputFilter = Component.OutputFilter;
                    lastWidth = decodedFrame->width;
                    lastHeight = decodedFrame->height;
                    lastFormat = decodedFrame->format;
                    lastSerial = PacketSerial;
                    lastFilterIndex = Container.vfilter_idx;
                    frameRate = ffmpeg.av_buffersink_get_frame_rate(outputFilter);
                }

                ret = ffmpeg.av_buffersrc_add_frame(inputFilter, decodedFrame);
                if (ret < 0)
                    goto the_end;

                while (ret >= 0)
                {
                    Container.frame_last_returned_time = ffmpeg.av_gettime_relative() / 1000000.0;

                    ret = ffmpeg.av_buffersink_get_frame_flags(outputFilter, decodedFrame, 0);
                    if (ret < 0)
                    {
                        if (ret == ffmpeg.AVERROR_EOF)
                            HasFinished = PacketSerial;
                        ret = 0;
                        break;
                    }

                    Container.frame_last_filter_delay = ffmpeg.av_gettime_relative() / 1000000.0 - Container.frame_last_returned_time;
                    if (Math.Abs(Container.frame_last_filter_delay) > Constants.AV_NOSYNC_THRESHOLD / 10.0)
                        Container.frame_last_filter_delay = 0;

                    var duration = (frameRate.num != 0 && frameRate.den != 0
                        ? ffmpeg.av_q2d(new AVRational() { num = frameRate.den, den = frameRate.num })
                        : 0);

                    var tb = ffmpeg.av_buffersink_get_time_base(outputFilter);
                    var pts = (decodedFrame->pts == ffmpeg.AV_NOPTS_VALUE)
                        ? double.NaN
                        : decodedFrame->pts * ffmpeg.av_q2d(tb);

                    ret = EnqueueFrame(decodedFrame, pts, duration, PacketSerial);
                    ffmpeg.av_frame_unref(decodedFrame);

                    if (Component.Packets.Serial != PacketSerial)
                        break;
                }

                if (ret < 0)
                    goto the_end;
            }
        the_end:

            ffmpeg.avfilter_graph_free(&filterGraph);
            ffmpeg.av_frame_free(&decodedFrame);
            return; // 0;
        }

        private int EnqueueFrame(AVFrame* sourceFrame, double pts, double duration, int serial)
        {
            var frameSlot = Component.Frames.PeekWriteable();

            if (frameSlot == null) return -1;

            frameSlot.Sar = sourceFrame->sample_aspect_ratio;
            frameSlot.uploaded = false;

            frameSlot.Width = sourceFrame->width;
            frameSlot.Height = sourceFrame->height;
            frameSlot.Format = sourceFrame->format;

            frameSlot.Pts = pts;
            frameSlot.Duration = duration;
            frameSlot.Position = sourceFrame->pkt_pos;
            frameSlot.Serial = serial;

            Container.Renderer.set_default_window_size(frameSlot.Width, frameSlot.Height, frameSlot.Sar);

            ffmpeg.av_frame_move_ref(frameSlot.FramePtr, sourceFrame);
            Component.Frames.Push();
            return 0;
        }

        private int DecodeFrame(out AVFrame* frame)
        {
            frame = null;
            var gotPicture = default(int);

            if ((gotPicture = DecodeFrame(out frame, out _)) < 0)
                return -1;

            if (gotPicture != 0)
            {
                var dpts = double.NaN;

                if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    dpts = ffmpeg.av_q2d(Component.Stream->time_base) * frame->pts;

                frame->sample_aspect_ratio = ffmpeg.av_guess_sample_aspect_ratio(Container.InputContext, Component.Stream, frame);

                if (Container.Options.framedrop > 0 || (Container.Options.framedrop != 0 && Container.MasterSyncMode != ClockSync.Video))
                {
                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        var diff = dpts - Container.MasterTime;
                        if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD &&
                            diff - Container.frame_last_filter_delay < 0 &&
                            PacketSerial == Container.VideoClock.Serial &&
                            Component.Packets.Count != 0)
                        {
                            Container.frame_drops_early++;
                            ffmpeg.av_frame_unref(frame);
                            gotPicture = 0;
                        }
                    }
                }
            }

            return gotPicture;
        }

        private int ConfigureFilters(ref AVFilterGraph* graph, string filterLiteral, AVFrame* frame)
        {
            var outputFormats = new List<int>(MediaRenderer.sdl_texture_map.Count);
            var softwareScalerFlags = string.Empty;

            int ret;
            AVFilterContext* sourceFilter = null, outputFilter = null, lastFilter = null;
            var codecParameters = Component.Stream->codecpar;
            var frameRate = ffmpeg.av_guess_frame_rate(Container.InputContext, Component.Stream, null);
            AVDictionaryEntry* e = null;

            for (var i = 0; i < Container.Renderer.renderer_info.num_texture_formats; i++)
            {
                foreach (var kvp in MediaRenderer.sdl_texture_map)
                {
                    if (kvp.Value == Container.Renderer.renderer_info.texture_formats[i])
                        outputFormats.Add((int)kvp.Key);
                }
            }

            //pix_fmts.Add(AVPixelFormat.AV_PIX_FMT_NONE);

            while ((e = ffmpeg.av_dict_get(Container.Options.sws_dict, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString(e->key);
                var value = Helpers.PtrToString(e->value);

                softwareScalerFlags = (key == "sws_flags")
                    ? $"flags={value}:{softwareScalerFlags}"
                    : $"{key}={value}:{softwareScalerFlags}";
            }

            if (string.IsNullOrWhiteSpace(softwareScalerFlags))
                softwareScalerFlags = null;

            graph->scale_sws_opts = softwareScalerFlags != null ? ffmpeg.av_strdup(softwareScalerFlags) : null;
            var sourceFilterArguments =
                $"video_size={frame->width}x{frame->height}" +
                $":pix_fmt={frame->format}" +
                $":time_base={Component.Stream->time_base.num}/{Component.Stream->time_base.den}" +
                $":pixel_aspect={codecParameters->sample_aspect_ratio.num}/{Math.Max(codecParameters->sample_aspect_ratio.den, 1)}";

            if (frameRate.num != 0 && frameRate.den != 0)
                sourceFilterArguments = $"{sourceFilterArguments}:frame_rate={frameRate.num}/{frameRate.den}";

            ret = ffmpeg.avfilter_graph_create_filter(
                &sourceFilter, ffmpeg.avfilter_get_by_name("buffer"), "video_source", sourceFilterArguments, null, graph);

            if (ret < 0)
                goto fail;

            ret = ffmpeg.avfilter_graph_create_filter(
                &outputFilter, ffmpeg.avfilter_get_by_name("buffersink"), "video_sink", null, null, graph);

            if (ret < 0)
                goto fail;

            ret = Helpers.av_opt_set_int_list(outputFilter, "pix_fmts", outputFormats.ToArray(), ffmpeg.AV_OPT_SEARCH_CHILDREN);
            if (ret < 0)
                goto fail;

            lastFilter = outputFilter;
            if (Container.Options.autorotate)
            {
                var theta = Helpers.get_rotation(Component.Stream);

                if (Math.Abs(theta - 90) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("transpose", "clock", ref graph, ref ret, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta - 180) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("hflip", null, ref graph, ref ret, ref lastFilter))
                        goto fail;

                    if (!Helpers.INSERT_FILT("vflip", null, ref graph, ref ret, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta - 270) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("transpose", "cclock", ref graph, ref ret, ref lastFilter))
                        goto fail;
                }
                else if (Math.Abs(theta) > 1.0)
                {
                    if (!Helpers.INSERT_FILT("rotate", $"{theta}*PI/180", ref graph, ref ret, ref lastFilter))
                        goto fail;
                }
            }

            if ((ret = MediaContainer.configure_filtergraph(ref graph, filterLiteral, sourceFilter, lastFilter)) < 0)
                goto fail;

            Component.InputFilter = sourceFilter;
            Component.OutputFilter = outputFilter;

        fail:
            return ret;
        }
    }

}
