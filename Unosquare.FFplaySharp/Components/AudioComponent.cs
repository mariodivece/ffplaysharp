namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;
    using System;
    using Unosquare.FFplaySharp.Primitives;

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

            AbortDecoder();
            Container.Renderer.CloseAudio();
            DisposeDecoder();

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

        protected override FrameQueue CreateFrameQueue() => new(Packets, Constants.SAMPLE_QUEUE_SIZE, true);

        public override unsafe void InitializeDecoder(AVCodecContext* codecContext)
        {
            base.InitializeDecoder(codecContext);

            var ic = Container.InputContext;
            if ((ic->iformat->flags & (ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK)) != 0 &&
                ic->iformat->read_seek.Pointer == IntPtr.Zero)
            {
                StartPts = Stream->start_time;
                StartPtsTimeBase = Stream->time_base;
            }
        }

        protected override void WorkerThreadMethod()
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
                        Helpers.cmp_audio_fmts(FilterSpec.SampleFormat, FilterSpec.Channels,
                                       (AVSampleFormat)frame->format, frame->channels) ||
                        FilterSpec.Layout != dec_channel_layout ||
                        FilterSpec.Frequency != frame->sample_rate ||
                        PacketSerial != last_serial;

                    if (reconfigure)
                    {
                        ffmpeg.av_get_channel_layout_string(buf1, bufLength, -1, (ulong)FilterSpec.Layout);
                        ffmpeg.av_get_channel_layout_string(buf2, bufLength, -1, (ulong)dec_channel_layout);
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Audio frame changed from " +
                           $"rate:{FilterSpec.Frequency} ch:{FilterSpec.Channels} fmt:{ffmpeg.av_get_sample_fmt_name(FilterSpec.SampleFormat)} layout:{Helpers.PtrToString(buf1)} serial:{last_serial} to " +
                           $"rate:{frame->sample_rate} ch:{frame->channels} fmt:{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)frame->format)} layout:{Helpers.PtrToString(buf2)} serial:{PacketSerial}\n");

                        FilterSpec.SampleFormat = (AVSampleFormat)frame->format;
                        FilterSpec.Channels = frame->channels;
                        FilterSpec.Layout = dec_channel_layout;
                        FilterSpec.Frequency = frame->sample_rate;
                        last_serial = PacketSerial;

                        if ((ret = configure_audio_filters(true)) < 0)
                            goto the_end;
                    }

                    if ((ret = ffmpeg.av_buffersrc_add_frame(InputFilter, frame)) < 0)
                        goto the_end;

                    while ((ret = ffmpeg.av_buffersink_get_frame_flags(OutputFilter, frame, 0)) >= 0)
                    {
                        tb = ffmpeg.av_buffersink_get_time_base(OutputFilter);

                        if ((af = Frames.PeekWriteable()) == null)
                            goto the_end;

                        af.Pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                        af.Position = frame->pkt_pos;
                        af.Serial = PacketSerial;
                        af.Duration = ffmpeg.av_q2d(new AVRational() { num = frame->nb_samples, den = frame->sample_rate });

                        ffmpeg.av_frame_move_ref(af.FramePtr, frame);
                        Frames.Push();

                        if (Packets.Serial != PacketSerial)
                            break;
                    }
                    if (ret == ffmpeg.AVERROR_EOF)
                        HasFinished = PacketSerial;
                }
            } while (ret >= 0 || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF);
        the_end:
            var agraph = FilterGraph;
            ffmpeg.avfilter_graph_free(&agraph);
            agraph = null;
            ffmpeg.av_frame_free(&frame);
        }
    }

}
