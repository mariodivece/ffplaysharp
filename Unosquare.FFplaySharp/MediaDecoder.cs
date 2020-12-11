namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Threading;

    public abstract unsafe class MediaDecoder
    {
        private readonly int ReorderPts;
        private readonly PacketQueue Packets;
        private readonly AutoResetEvent EmptyQueueEvent;

        private PacketHolder PendingPacket;
        private bool IsPacketPending;
        private long NextPts;
        private AVRational NextPtsTimeBase;
        private Thread Worker;

        protected MediaDecoder(MediaComponent component, AVCodecContext* codecContext)
        {
            Component = component;
            Container = component.Container;
            CodecContext = codecContext;
            Packets = component.Packets;
            EmptyQueueEvent = component.Container.continue_read_thread;
            StartPts = ffmpeg.AV_NOPTS_VALUE;
            PacketSerial = -1;
            ReorderPts = component.Container.Options.decoder_reorder_pts;
            StartPtsTimeBase = new();
        }

        public AVCodecContext* CodecContext { get; private set; }

        public MediaComponent Component { get; }

        public MediaContainer Container { get; }

        public int PacketSerial { get; private set; }

        public int HasFinished { get; set; }

        public long StartPts { get; set; }

        public AVRational StartPtsTimeBase { get; set; }

        public int DecodeFrame(out AVFrame* frame, out AVSubtitle* sub)
        {
            int ret = ffmpeg.AVERROR(ffmpeg.EAGAIN);
            sub = null;
            frame = null;

            for (; ; )
            {
                PacketHolder currentPacket = null;

                if (Packets.Serial == PacketSerial)
                {
                    do
                    {
                        if (Packets.IsClosed)
                            return -1;

                        switch (CodecContext->codec_type)
                        {
                            case AVMediaType.AVMEDIA_TYPE_VIDEO:
                                if (frame == null) frame = ffmpeg.av_frame_alloc();

                                ret = ffmpeg.avcodec_receive_frame(CodecContext, frame);
                                if (ret >= 0)
                                {
                                    if (ReorderPts == -1)
                                    {
                                        frame->pts = frame->best_effort_timestamp;
                                    }
                                    else if (ReorderPts == 0)
                                    {
                                        frame->pts = frame->pkt_dts;
                                    }
                                }
                                break;
                            case AVMediaType.AVMEDIA_TYPE_AUDIO:
                                if (frame == null) frame = ffmpeg.av_frame_alloc();

                                ret = ffmpeg.avcodec_receive_frame(CodecContext, frame);
                                if (ret >= 0)
                                {
                                    AVRational tb = new();
                                    tb.num = 1;
                                    tb.den = frame->sample_rate;

                                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                                        frame->pts = ffmpeg.av_rescale_q(frame->pts, CodecContext->pkt_timebase, tb);
                                    else if (NextPts != ffmpeg.AV_NOPTS_VALUE)
                                        frame->pts = ffmpeg.av_rescale_q(NextPts, NextPtsTimeBase, tb);
                                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                                    {
                                        NextPts = frame->pts + frame->nb_samples;
                                        NextPtsTimeBase = tb;
                                    }
                                }
                                break;
                        }
                        if (ret == ffmpeg.AVERROR_EOF)
                        {
                            HasFinished = PacketSerial;
                            ffmpeg.avcodec_flush_buffers(CodecContext);
                            return 0;
                        }
                        if (ret >= 0)
                            return 1;
                    } while (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN));
                }

                do
                {
                    if (Packets.Count == 0)
                        EmptyQueueEvent.Set();

                    if (IsPacketPending)
                    {
                        currentPacket = PendingPacket;
                        IsPacketPending = false;
                    }
                    else
                    {
                        currentPacket = Packets.Get(true);
                        if (Packets.IsClosed)
                            return -1;

                        if (currentPacket != null)
                            PacketSerial = currentPacket.Serial;
                    }

                    if (Packets.Serial == PacketSerial)
                        break;

                    currentPacket?.Dispose();

                } while (true);

                if (currentPacket.IsFlushPacket)
                {
                    ffmpeg.avcodec_flush_buffers(CodecContext);
                    HasFinished = 0;
                    NextPts = StartPts;
                    NextPtsTimeBase = StartPtsTimeBase;
                }
                else
                {
                    if (CodecContext->codec_type == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        int got_frame = 0;
                        sub = (AVSubtitle*)ffmpeg.av_malloc((ulong)sizeof(AVSubtitle));
                        ret = ffmpeg.avcodec_decode_subtitle2(CodecContext, sub, &got_frame, currentPacket.PacketPtr);

                        if (ret < 0)
                        {
                            ret = ffmpeg.AVERROR(ffmpeg.EAGAIN);
                        }
                        else
                        {
                            if (got_frame != 0 && currentPacket.PacketPtr->data == null)
                            {
                                IsPacketPending = true;
                                PendingPacket = new PacketHolder(ffmpeg.av_packet_clone(currentPacket.PacketPtr));
                            }

                            ret = got_frame != 0 ? 0 : (currentPacket.PacketPtr->data != null ? ffmpeg.AVERROR(ffmpeg.EAGAIN) : ffmpeg.AVERROR_EOF);
                        }
                    }
                    else
                    {
                        if (ffmpeg.avcodec_send_packet(CodecContext, currentPacket.PacketPtr) == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            ffmpeg.av_log(CodecContext, ffmpeg.AV_LOG_ERROR, "Receive_frame and send_packet both returned EAGAIN, which is an API violation.\n");
                            IsPacketPending = true;
                            PendingPacket = new PacketHolder(ffmpeg.av_packet_clone(currentPacket.PacketPtr));
                        }
                    }

                    currentPacket?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            PendingPacket?.Dispose();
            var codecContext = CodecContext;
            ffmpeg.avcodec_free_context(&codecContext);
            CodecContext = null;
        }

        public void Abort(FrameQueue fq)
        {
            Packets.Close();
            fq.SignalChanged();
            Worker.Join();
            Worker = null;
            Packets.Clear();
        }

        public abstract AVMediaType MediaType { get; }

        protected void Start(ThreadStart workerMethod, string threadName)
        {
            Packets.Open();
            Worker = new Thread(workerMethod) { Name = threadName, IsBackground = true };
            Worker.Start();
        }

        public abstract void Start();
    }

    public unsafe sealed class AudioDecoder : MediaDecoder
    {
        public AudioDecoder(MediaContainer container, AVCodecContext* codecContext)
            : base(container.Audio, codecContext)
        {
            Component = container.Audio;
        }

        public new AudioComponent Component { get; }

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_AUDIO;

        public override void Start() => base.Start(WorkerThreadMethod, "AudioDecoder");

        private void WorkerThreadMethod()
        {
            FrameHolder af;
            var last_serial = -1;
            int got_frame = 0;
            int ret = 0;

            var frame = ffmpeg.av_frame_alloc();

            const int bufLength = 1024;
            var buf1 = stackalloc byte[bufLength];
            var buf2 = stackalloc byte[bufLength];

            do
            {
                if ((got_frame = DecodeFrame(out frame, out _)) < 0)
                    goto the_end;

                if (got_frame != 0)
                {
                    var tb = new AVRational() { num = 1, den = frame->sample_rate };
                    var dec_channel_layout = (long)Helpers.get_valid_channel_layout(frame->channel_layout, frame->channels);

                    var reconfigure =
                        Helpers.cmp_audio_fmts(Component.FilterSpec.SampleFormat, Component.FilterSpec.Channels,
                                       (AVSampleFormat)frame->format, frame->channels) ||
                        Component.FilterSpec.Layout != dec_channel_layout ||
                        Component.FilterSpec.Frequency != frame->sample_rate ||
                        Component.Decoder.PacketSerial != last_serial;

                    if (reconfigure)
                    {
                        ffmpeg.av_get_channel_layout_string(buf1, bufLength, -1, (ulong)Component.FilterSpec.Layout);
                        ffmpeg.av_get_channel_layout_string(buf2, bufLength, -1, (ulong)dec_channel_layout);
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Audio frame changed from " +
                           $"rate:{Component.FilterSpec.Frequency} ch:{Component.FilterSpec.Channels} fmt:{ffmpeg.av_get_sample_fmt_name(Component.FilterSpec.SampleFormat)} layout:{Helpers.PtrToString(buf1)} serial:{last_serial} to " +
                           $"rate:{frame->sample_rate} ch:{frame->channels} fmt:{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)frame->format)} layout:{Helpers.PtrToString(buf2)} serial:{Component.Decoder.PacketSerial}\n");

                        Component.FilterSpec.SampleFormat = (AVSampleFormat)frame->format;
                        Component.FilterSpec.Channels = frame->channels;
                        Component.FilterSpec.Layout = dec_channel_layout;
                        Component.FilterSpec.Frequency = frame->sample_rate;
                        last_serial = Component.Decoder.PacketSerial;

                        if ((ret = Container.configure_audio_filters(true)) < 0)
                            goto the_end;
                    }

                    if ((ret = ffmpeg.av_buffersrc_add_frame(Component.InputFilter, frame)) < 0)
                        goto the_end;

                    while ((ret = ffmpeg.av_buffersink_get_frame_flags(Component.OutputFilter, frame, 0)) >= 0)
                    {
                        tb = ffmpeg.av_buffersink_get_time_base(Component.OutputFilter);

                        if ((af = Component.Frames.PeekWriteable()) == null)
                            goto the_end;

                        af.Pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                        af.Position = frame->pkt_pos;
                        af.Serial = Component.Decoder.PacketSerial;
                        af.Duration = ffmpeg.av_q2d(new AVRational() { num = frame->nb_samples, den = frame->sample_rate });

                        ffmpeg.av_frame_move_ref(af.FramePtr, frame);
                        Component.Frames.Push();

                        if (Component.Packets.Serial != Component.Decoder.PacketSerial)
                            break;
                    }
                    if (ret == ffmpeg.AVERROR_EOF)
                        Component.Decoder.HasFinished = Component.Decoder.PacketSerial;
                }
            } while (ret >= 0 || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF);
        the_end:
            var agraph = Container.agraph;
            ffmpeg.avfilter_graph_free(&agraph);
            agraph = null;
            ffmpeg.av_frame_free(&frame);
        }
    }

    public unsafe sealed class VideoDecoder : MediaDecoder
    {
        public VideoDecoder(MediaContainer container, AVCodecContext* codecContext)
            : base(container.Video, codecContext)
        {
            Component = container.Video;
        }

        public new VideoComponent Component { get; }

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_VIDEO;

        public override void Start() => base.Start(VideoWorkerThreadMethod, "VideoDecoder");

        private void VideoWorkerThreadMethod()
        {
            AVFrame* frame = null; // ffmpeg.av_frame_alloc();
            double pts;
            double duration;
            int ret;
            AVRational tb = Component.Stream->time_base;
            AVRational frame_rate = ffmpeg.av_guess_frame_rate(Container.InputContext, Component.Stream, null);

            AVFilterGraph* graph = null;
            AVFilterContext* filt_out = null, filt_in = null;
            int last_w = 0;
            int last_h = 0;
            int last_format = -2;
            int last_serial = -1;
            int last_vfilter_idx = 0;

            for (; ; )
            {
                ret = get_video_frame(out frame);
                if (ret < 0)
                    goto the_end;

                if (ret == 0)
                    continue;

                if (last_w != frame->width
                    || last_h != frame->height
                    || last_format != frame->format
                    || last_serial != PacketSerial
                    || last_vfilter_idx != Container.vfilter_idx)
                {
                    var lastFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)last_format);
                    lastFormat ??= "none";

                    var frameFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)frame->format);
                    frameFormat ??= "none";

                    ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Video frame changed from size:{last_w}x%{last_h} format:{lastFormat} serial:{last_serial} to " +
                           $"size:{frame->width}x{frame->height} format:{frameFormat} serial:{PacketSerial}\n");

                    ffmpeg.avfilter_graph_free(&graph);
                    graph = ffmpeg.avfilter_graph_alloc();
                    if (graph == null)
                    {
                        ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                        goto the_end;
                    }
                    graph->nb_threads = Container.Options.filter_nbthreads;
                    if ((ret = configure_video_filters(graph, Container, Container.Options.vfilters_list.Count > 0
                        ? Container.Options.vfilters_list[Container.vfilter_idx]
                        : null, frame)) < 0)
                    {
                        var evt = new SDL.SDL_Event()
                        {
                            type = (SDL.SDL_EventType)Constants.FF_QUIT_EVENT,
                        };

                        // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                        SDL.SDL_PushEvent(ref evt);
                        goto the_end;
                    }

                    filt_in = Component.InputFilter;
                    filt_out = Component.OutputFilter;
                    last_w = frame->width;
                    last_h = frame->height;
                    last_format = frame->format;
                    last_serial = PacketSerial;
                    last_vfilter_idx = Container.vfilter_idx;
                    frame_rate = ffmpeg.av_buffersink_get_frame_rate(filt_out);
                }

                ret = ffmpeg.av_buffersrc_add_frame(filt_in, frame);
                if (ret < 0)
                    goto the_end;

                while (ret >= 0)
                {
                    Container.frame_last_returned_time = ffmpeg.av_gettime_relative() / 1000000.0;

                    ret = ffmpeg.av_buffersink_get_frame_flags(filt_out, frame, 0);
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

                    tb = ffmpeg.av_buffersink_get_time_base(filt_out);
                    duration = (frame_rate.num != 0 && frame_rate.den != 0 ? ffmpeg.av_q2d(new AVRational() { num = frame_rate.den, den = frame_rate.num }) : 0);
                    pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                    ret = queue_picture(frame, pts, duration, frame->pkt_pos, PacketSerial);
                    ffmpeg.av_frame_unref(frame);

                    if (Component.Packets.Serial != PacketSerial)
                        break;
                }

                if (ret < 0)
                    goto the_end;
            }
        the_end:

            ffmpeg.avfilter_graph_free(&graph);
            ffmpeg.av_frame_free(&frame);
            return; // 0;
        }

        private int queue_picture(AVFrame* src_frame, double pts, double duration, long pos, int serial)
        {
            var vp = Component.Frames.PeekWriteable();

            if (vp == null) return -1;

            vp.Sar = src_frame->sample_aspect_ratio;
            vp.uploaded = false;

            vp.Width = src_frame->width;
            vp.Height = src_frame->height;
            vp.Format = src_frame->format;

            vp.Pts = pts;
            vp.Duration = duration;
            vp.Position = pos;
            vp.Serial = serial;

            Container.Renderer.set_default_window_size(vp.Width, vp.Height, vp.Sar);

            ffmpeg.av_frame_move_ref(vp.FramePtr, src_frame);
            Component.Frames.Push();
            return 0;
        }

        private int get_video_frame(out AVFrame* frame)
        {
            frame = null;
            int got_picture;

            if ((got_picture = DecodeFrame(out frame, out _)) < 0)
                return -1;

            if (got_picture != 0)
            {
                double dpts = double.NaN;

                if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    dpts = ffmpeg.av_q2d(Component.Stream->time_base) * frame->pts;

                frame->sample_aspect_ratio = ffmpeg.av_guess_sample_aspect_ratio(Container.InputContext, Component.Stream, frame);

                if (Container.Options.framedrop > 0 || (Container.Options.framedrop != 0 && Container.MasterSyncMode != ClockSync.Video))
                {
                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        double diff = dpts - Container.MasterTime;
                        if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD &&
                            diff - Container.frame_last_filter_delay < 0 &&
                            PacketSerial == Container.VideoClock.Serial &&
                            Component.Packets.Count != 0)
                        {
                            Container.frame_drops_early++;
                            ffmpeg.av_frame_unref(frame);
                            got_picture = 0;
                        }
                    }
                }
            }

            return got_picture;
        }

        private int configure_video_filters(AVFilterGraph* graph, MediaContainer container, string vfilters, AVFrame* frame)
        {
            // enum AVPixelFormat pix_fmts[FF_ARRAY_ELEMS(sdl_texture_format_map)];
            var pix_fmts = new List<int>(MediaRenderer.sdl_texture_map.Count);
            string sws_flags_str = string.Empty;
            string buffersrc_args = string.Empty;
            int ret;
            AVFilterContext* filt_src = null, filt_out = null, last_filter = null;
            AVCodecParameters* codecpar = container.Video.Stream->codecpar;
            AVRational fr = ffmpeg.av_guess_frame_rate(container.InputContext, container.Video.Stream, null);
            AVDictionaryEntry* e = null;

            for (var i = 0; i < Container.Renderer.renderer_info.num_texture_formats; i++)
            {
                foreach (var kvp in MediaRenderer.sdl_texture_map)
                {
                    if (kvp.Value == Container.Renderer.renderer_info.texture_formats[i])
                    {
                        pix_fmts.Add((int)kvp.Key);
                    }
                }
            }

            //pix_fmts.Add(AVPixelFormat.AV_PIX_FMT_NONE);

            while ((e = ffmpeg.av_dict_get(container.Options.sws_dict, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString(e->key);
                var value = Helpers.PtrToString(e->value);
                if (key == "sws_flags")
                    sws_flags_str = $"flags={value}:{sws_flags_str}";
                else
                    sws_flags_str = $"{key}={value}:{sws_flags_str}";
            }

            if (string.IsNullOrWhiteSpace(sws_flags_str))
                sws_flags_str = null;

            graph->scale_sws_opts = sws_flags_str != null ? ffmpeg.av_strdup(sws_flags_str) : null;
            buffersrc_args = $"video_size={frame->width}x{frame->height}:pix_fmt={frame->format}:time_base={container.Video.Stream->time_base.num}/{container.Video.Stream->time_base.den}:pixel_aspect={codecpar->sample_aspect_ratio.num}/{Math.Max(codecpar->sample_aspect_ratio.den, 1)}";

            if (fr.num != 0 && fr.den != 0)
                buffersrc_args = $"{buffersrc_args}:frame_rate={fr.num}/{fr.den}";

            if ((ret = ffmpeg.avfilter_graph_create_filter(&filt_src,
                                                    ffmpeg.avfilter_get_by_name("buffer"),
                                                    "ffplay_buffer", buffersrc_args, null,
                                                    graph)) < 0)
                goto fail;

            ret = ffmpeg.avfilter_graph_create_filter(&filt_out,
                                               ffmpeg.avfilter_get_by_name("buffersink"),
                                               "ffplay_buffersink", null, null, graph);
            if (ret < 0)
                goto fail;

            if ((ret = Helpers.av_opt_set_int_list(filt_out, "pix_fmts", pix_fmts.ToArray(), ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto fail;

            last_filter = filt_out;
            if (container.Options.autorotate)
            {
                double theta = Helpers.get_rotation(container.Video.Stream);

                if (Math.Abs(theta - 90) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("transpose", "clock", ref graph, ref ret, ref last_filter))
                        goto fail;
                }
                else if (Math.Abs(theta - 180) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("hflip", null, ref graph, ref ret, ref last_filter))
                        goto fail;

                    if (!Helpers.INSERT_FILT("vflip", null, ref graph, ref ret, ref last_filter))
                        goto fail;
                }
                else if (Math.Abs(theta - 270) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("transpose", "cclock", ref graph, ref ret, ref last_filter))
                        goto fail;
                }
                else if (Math.Abs(theta) > 1.0)
                {
                    if (!Helpers.INSERT_FILT("rotate", $"{theta}*PI/180", ref graph, ref ret, ref last_filter))
                        goto fail;
                }
            }

            if ((ret = MediaContainer.configure_filtergraph(graph, vfilters, filt_src, last_filter)) < 0)
                goto fail;

            container.Video.InputFilter = filt_src;
            container.Video.OutputFilter = filt_out;

        fail:
            return ret;
        }
    }

    public unsafe sealed class SubtitleDecoder : MediaDecoder
    {
        public SubtitleDecoder(MediaContainer container, AVCodecContext* codecContext)
            : base(container.Subtitle, codecContext)
        {
            Component = container.Subtitle;
        }

        public new SubtitleComponent Component { get; }

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_SUBTITLE;

        public override void Start() => base.Start(SubtitleWorkerThreadMethod, "SubtitleDecoder");

        private void SubtitleWorkerThreadMethod()
        {
            FrameHolder sp;
            int got_subtitle;
            double pts;

            for (; ; )
            {
                if ((sp = Component.Frames.PeekWriteable()) == null)
                    return; // 0;

                if ((got_subtitle = DecodeFrame(out _, out var spsub)) < 0)
                    break;
                else
                    sp.SubtitlePtr = spsub;

                pts = 0;

                if (got_subtitle != 0 && sp.SubtitlePtr->format == 0)
                {
                    if (sp.SubtitlePtr->pts != ffmpeg.AV_NOPTS_VALUE)
                        pts = sp.SubtitlePtr->pts / (double)ffmpeg.AV_TIME_BASE;
                    sp.Pts = pts;
                    sp.Serial = PacketSerial;
                    sp.Width = CodecContext->width;
                    sp.Height = CodecContext->height;
                    sp.uploaded = false;

                    /* now we can update the picture count */
                    Component.Frames.Push();
                }
                else if (got_subtitle != 0)
                {
                    ffmpeg.avsubtitle_free(sp.SubtitlePtr);
                }
            }

            return; // 0
        }
    }
}
