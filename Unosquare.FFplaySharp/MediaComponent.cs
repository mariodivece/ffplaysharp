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
            Frames = CreateFrameQueue();
        }

        public MediaContainer Container { get; }

        public PacketQueue Packets { get; }

        public FrameQueue Frames { get; }

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

        protected abstract FrameQueue CreateFrameQueue();
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

        protected override FrameQueue CreateFrameQueue() => new FrameQueue(Packets, Constants.VIDEO_PICTURE_QUEUE_SIZE, true);
    }

    public unsafe sealed class AudioComponent : FilteringMediaComponent
    {
        public AVFilterGraph* FilterGraph = null;              // audio filter graph

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

        public int configure_audio_filters(bool forceOutputFormat)
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

            var audioFilterGraph = FilterGraph;
            // TODO: sometimes agraph has weird memory.
            if (audioFilterGraph != null && audioFilterGraph->nb_filters > 0)
                ffmpeg.avfilter_graph_free(&audioFilterGraph);
            FilterGraph = ffmpeg.avfilter_graph_alloc();
            FilterGraph->nb_threads = o.filter_nbthreads;

            while ((e = ffmpeg.av_dict_get(o.swr_opts, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString((IntPtr)e->key);
                var value = Helpers.PtrToString((IntPtr)e->value);
                aresample_swr_opts = $"{key}={value}:{aresample_swr_opts}";
            }

            if (string.IsNullOrWhiteSpace(aresample_swr_opts))
                aresample_swr_opts = null;

            ffmpeg.av_opt_set(FilterGraph, "aresample_swr_opts", aresample_swr_opts, 0);
            asrc_args = $"sample_rate={FilterSpec.Frequency}:sample_fmt={ffmpeg.av_get_sample_fmt_name(FilterSpec.SampleFormat)}:" +
                $"channels={FilterSpec.Channels}:time_base={1}/{FilterSpec.Frequency}";

            if (FilterSpec.Layout != 0)
                asrc_args = $"{asrc_args}:channel_layout=0x{FilterSpec.Layout:x16}";

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asrc,
                                               ffmpeg.avfilter_get_by_name("abuffer"), "ffplay_abuffer",
                                               asrc_args, null, FilterGraph);
            if (ret < 0)
                goto end;

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asink,
                                               ffmpeg.avfilter_get_by_name("abuffersink"), "ffplay_abuffersink",
                                               null, null, FilterGraph);
            if (ret < 0)
                goto end;

            if ((ret = Helpers.av_opt_set_int_list(filt_asink, "sample_fmts", sample_fmts, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if ((ret = ffmpeg.av_opt_set_int(filt_asink, "all_channel_counts", 1, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if (forceOutputFormat)
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

            if ((ret = MediaContainer.configure_filtergraph(ref FilterGraph, afilters, filt_asrc, filt_asink)) < 0)
                goto end;

            InputFilter = filt_asrc;
            OutputFilter = filt_asink;

        end:
            if (ret < 0)
            {
                var audioGraphRef = FilterGraph;
                ffmpeg.avfilter_graph_free(&audioGraphRef);
                FilterGraph = null;
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

        protected override FrameQueue CreateFrameQueue() => new FrameQueue(Packets, Constants.SAMPLE_QUEUE_SIZE, true);
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

        protected override FrameQueue CreateFrameQueue() => new FrameQueue(Packets, Constants.SUBPICTURE_QUEUE_SIZE, false);
    }
}
