namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;

    public abstract unsafe class MediaComponent
    {
        protected MediaComponent(MediaContainer container)
        {
            Container = container;
            Packets = new(this);
        }

        public MediaContainer Container { get; }

        public PacketQueue Packets { get; }

        public FrameQueue Frames;

        public MediaDecoder Decoder { get; set; }

        public AVStream* Stream;

        public int StreamIndex;

        public int LastStreamIndex;

        public abstract AVMediaType MediaType { get; }

        public bool IsAudio => MediaType == AVMediaType.AVMEDIA_TYPE_AUDIO;
        public bool IsVideo => MediaType == AVMediaType.AVMEDIA_TYPE_VIDEO;

        public bool IsSubtitle => MediaType == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

        public bool HasEnoughPackets
        {
            get
            {
                return StreamIndex < 0 ||
                   Packets.IsClosed ||
                   (Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0 ||
                   Packets.Count > Constants.MIN_FRAMES && (Packets.Duration == 0 ||
                   ffmpeg.av_q2d(Stream->time_base) * Packets.Duration > 1.0);
            }
        }

        public virtual void Close()
        {
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext->nb_streams)
                return;

            Decoder?.Abort();
            Decoder?.Dispose();
            Container.InputContext->streams[StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
            Stream = null;
            StreamIndex = -1;
        }
    }

    public abstract unsafe class FilteringMediaComponent : MediaComponent
    {
        protected FilteringMediaComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public AVFilterContext* InputFilter;
        public AVFilterContext* OutputFilter;
    }

    public unsafe sealed class VideoComponent : FilteringMediaComponent
    {
        public VideoComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwsContext* ConvertContext;

        public new VideoDecoder Decoder { get; set; }

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_VIDEO;

        public int configure_video_filters(ref AVFilterGraph* graph, MediaContainer container, string vfilters, AVFrame* frame)
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

            if ((ret = MediaContainer.configure_filtergraph(ref graph, vfilters, filt_src, last_filter)) < 0)
                goto fail;

            container.Video.InputFilter = filt_src;
            container.Video.OutputFilter = filt_out;

        fail:
            return ret;
        }
    }

    public unsafe sealed class AudioComponent : FilteringMediaComponent
    {
        public AVFilterGraph* agraph = null;              // audio filter graph

        public AudioComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwrContext* ConvertContext;

        public AudioParams SourceSpec = new();
        public AudioParams FilterSpec = new();
        public AudioParams TargetSpec = new();

        public new AudioDecoder Decoder { get; set; }

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_AUDIO;

        public int configure_audio_filters(bool force_output_format)
        {
            var o = Container.Options;
            var afilters = o.afilters;
            var sample_fmts = new[] { (int)AVSampleFormat.AV_SAMPLE_FMT_S16 };
            var sample_rates = new[] { 0 };
            var channel_layouts = new[] { 0L };
            var channels = new[] { 0 };

            AVFilterContext* filt_asrc = null, filt_asink = null;
            string aresample_swr_opts = string.Empty;
            AVDictionaryEntry* e = null;
            string asrc_args = null;
            int ret;

            var audioFilterGraph = agraph;
            // TODO: sometimes agraph has weird memory.
            if (audioFilterGraph != null && audioFilterGraph->nb_filters > 0)
                ffmpeg.avfilter_graph_free(&audioFilterGraph);
            agraph = ffmpeg.avfilter_graph_alloc();
            agraph->nb_threads = o.filter_nbthreads;

            while ((e = ffmpeg.av_dict_get(o.swr_opts, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString((IntPtr)e->key);
                var value = Helpers.PtrToString((IntPtr)e->value);
                aresample_swr_opts = $"{key}={value}:{aresample_swr_opts}";
            }

            if (string.IsNullOrWhiteSpace(aresample_swr_opts))
                aresample_swr_opts = null;

            ffmpeg.av_opt_set(agraph, "aresample_swr_opts", aresample_swr_opts, 0);
            asrc_args = $"sample_rate={FilterSpec.Frequency}:sample_fmt={ffmpeg.av_get_sample_fmt_name(FilterSpec.SampleFormat)}:" +
                $"channels={FilterSpec.Channels}:time_base={1}/{FilterSpec.Frequency}";

            if (FilterSpec.Layout != 0)
                asrc_args = $"{asrc_args}:channel_layout=0x{FilterSpec.Layout:x16}";

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asrc,
                                               ffmpeg.avfilter_get_by_name("abuffer"), "ffplay_abuffer",
                                               asrc_args, null, agraph);
            if (ret < 0)
                goto end;

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asink,
                                               ffmpeg.avfilter_get_by_name("abuffersink"), "ffplay_abuffersink",
                                               null, null, agraph);
            if (ret < 0)
                goto end;

            if ((ret = Helpers.av_opt_set_int_list(filt_asink, "sample_fmts", sample_fmts, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if ((ret = ffmpeg.av_opt_set_int(filt_asink, "all_channel_counts", 1, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if (force_output_format)
            {
                channel_layouts[0] = Convert.ToInt32(TargetSpec.Layout);
                channels[0] = TargetSpec.Layout != 0 ? -1 : TargetSpec.Channels;
                sample_rates[0] = TargetSpec.Frequency;
                if ((ret = ffmpeg.av_opt_set_int(filt_asink, "all_channel_counts", 0, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "channel_layouts", channel_layouts, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "channel_counts", channels, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "sample_rates", sample_rates, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
            }

            if ((ret = MediaContainer.configure_filtergraph(ref agraph, afilters, filt_asrc, filt_asink)) < 0)
                goto end;

            InputFilter = filt_asrc;
            OutputFilter = filt_asink;

        end:
            if (ret < 0)
            {
                var audioGraphRef = agraph;
                ffmpeg.avfilter_graph_free(&audioGraphRef);
                agraph = null;
            }

            return ret;
        }

        public override void Close()
        {
            if (StreamIndex < 0 || StreamIndex >= Container.InputContext->nb_streams)
                return;

            Decoder?.Abort();
            Container.Renderer.CloseAudio();
            Decoder?.Dispose();

            var contextPointer = ConvertContext;
            ffmpeg.swr_free(&contextPointer);
            ConvertContext = null;

            if (Container.audio_buf1 != null)
                ffmpeg.av_free(Container.audio_buf1);

            Container.audio_buf1 = null;
            Container.audio_buf1_size = 0;
            Container.audio_buf = null;

            Container.InputContext->streams[StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
            Stream = null;
            StreamIndex = -1;
        }
    }

    public unsafe sealed class SubtitleComponent : MediaComponent
    {
        public SubtitleComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwsContext* ConvertContext;

        public new SubtitleDecoder Decoder { get; set; }

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_SUBTITLE;
    }
}
