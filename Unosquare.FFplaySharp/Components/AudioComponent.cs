﻿namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;
    using System;
    using System.Diagnostics;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class AudioComponent : FilteringMediaComponent
    {
        public double audio_clock;
        public double last_audio_clock = 0;
        public double audio_diff_cum; /* used for AV difference average computation */
        public double audio_diff_threshold;
        public double audio_diff_avg_coef;
        public int audio_diff_avg_count;

        public AudioComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public SwrContext* ConvertContext { get; private set; }

        public AudioParams SourceSpec = new();
        public AudioParams FilterSpec = new();
        public AudioParams TargetSpec = new();

        public override AVMediaType MediaType => AVMediaType.AVMEDIA_TYPE_AUDIO;

        public int configure_audio_filters(bool forceOutputFormat)
        {
            var o = Container.Options;
            
            var sample_fmts = new[] { (int)AVSampleFormat.AV_SAMPLE_FMT_S16 };
            // var sample_rates = new[] { 0 };
            // var channel_layouts = new[] { 0L };
            // var channels = new[] { 0 };

            AVFilterContext* inputFilterContext = null;
            AVFilterContext* outputFilterContext = null;
            AVDictionaryEntry* e = null;

            string resamplerOptions = string.Empty;
            string asrc_args = null;
            int ret;

            {
                var audioFilterGraph = FilterGraph;
                // TODO: sometimes agraph has weird memory.
                if (audioFilterGraph != null && audioFilterGraph->nb_filters > 0)
                    ffmpeg.avfilter_graph_free(&audioFilterGraph);
            }

            FilterGraph = ffmpeg.avfilter_graph_alloc();
            FilterGraph->nb_threads = o.filter_nbthreads;

            while ((e = ffmpeg.av_dict_get(o.swr_opts, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Helpers.PtrToString((IntPtr)e->key);
                var value = Helpers.PtrToString((IntPtr)e->value);
                resamplerOptions = $"{key}={value}:{resamplerOptions}";
            }

            if (string.IsNullOrWhiteSpace(resamplerOptions))
                resamplerOptions = null;

            ffmpeg.av_opt_set(FilterGraph, "aresample_swr_opts", resamplerOptions, 0);
            asrc_args = $"sample_rate={FilterSpec.Frequency}:sample_fmt={ffmpeg.av_get_sample_fmt_name(FilterSpec.SampleFormat)}:" +
                $"channels={FilterSpec.Channels}:time_base={1}/{FilterSpec.Frequency}";

            if (FilterSpec.Layout != 0)
                asrc_args = $"{asrc_args}:channel_layout=0x{FilterSpec.Layout:x16}";

            ret = ffmpeg.avfilter_graph_create_filter(&inputFilterContext,
                                               ffmpeg.avfilter_get_by_name("abuffer"), "ffplay_abuffer",
                                               asrc_args, null, FilterGraph);
            if (ret < 0)
                goto end;

            ret = ffmpeg.avfilter_graph_create_filter(&outputFilterContext,
                                               ffmpeg.avfilter_get_by_name("abuffersink"), "ffplay_abuffersink",
                                               null, null, FilterGraph);
            if (ret < 0)
                goto end;

            if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "sample_fmts", sample_fmts, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if ((ret = ffmpeg.av_opt_set_int(outputFilterContext, "all_channel_counts", 1, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if (forceOutputFormat)
            {
                var outputChannelLayout = new[] { Convert.ToInt32(TargetSpec.Layout) };
                var outputChannelCount = new[] { TargetSpec.Layout != 0 ? -1 : TargetSpec.Channels };
                var outputSampleRates = new[] { TargetSpec.Frequency };

                if ((ret = ffmpeg.av_opt_set_int(outputFilterContext, "all_channel_counts", 0, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "channel_layouts", outputChannelLayout, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "channel_counts", outputChannelCount, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(outputFilterContext, "sample_rates", outputSampleRates, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
            }

            var filterGraphLiteral = o.afilters;
            if ((ret = configure_filtergraph(FilterGraph, filterGraphLiteral, inputFilterContext, outputFilterContext)) < 0)
                goto end;

            InputFilter = inputFilterContext;
            OutputFilter = outputFilterContext;

        end:
            if (ret < 0)
            {
                var audioFilterGraph = FilterGraph;
                ffmpeg.avfilter_graph_free(&audioFilterGraph);
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
        public int audio_decode_frame()
        {
            int data_size, resampled_data_size;
            long dec_channel_layout;
            double audio_clock0;
            FrameHolder af;

            if (Container.IsPaused)
                return -1;

            do
            {
                while (Container.Audio.Frames.PendingCount == 0)
                {
                    if ((ffmpeg.av_gettime_relative() - Container.Renderer.audio_callback_time) > 1000000L * Container.audio_hw_buf_size / TargetSpec.BytesPerSecond / 2)
                        return -1;
                    ffmpeg.av_usleep(1000);
                }

                if ((af = Frames.PeekReadable()) == null)
                    return -1;

                Frames.Next();

            } while (af.Serial != Packets.Serial);

            data_size = ffmpeg.av_samples_get_buffer_size(null, af.FramePtr->channels,
                                                   af.FramePtr->nb_samples,
                                                   (AVSampleFormat)af.FramePtr->format, 1);

            dec_channel_layout =
                (af.FramePtr->channel_layout != 0 && af.FramePtr->channels == ffmpeg.av_get_channel_layout_nb_channels(af.FramePtr->channel_layout))
                ? (long)af.FramePtr->channel_layout
                : ffmpeg.av_get_default_channel_layout(af.FramePtr->channels);
            var wantedSampleCount = synchronize_audio(af.FramePtr->nb_samples);

            if (af.FramePtr->format != (int)SourceSpec.SampleFormat ||
                dec_channel_layout != SourceSpec.Layout ||
                af.FramePtr->sample_rate != SourceSpec.Frequency ||
                (wantedSampleCount != af.FramePtr->nb_samples && ConvertContext == null))
            {
                var convertContext = ConvertContext;
                ffmpeg.swr_free(&convertContext);
                ConvertContext = null;

                ConvertContext = ffmpeg.swr_alloc_set_opts(null,
                                                 TargetSpec.Layout, TargetSpec.SampleFormat, TargetSpec.Frequency,
                                                 dec_channel_layout, (AVSampleFormat)af.FramePtr->format, af.FramePtr->sample_rate,
                                                 0, null);

                if (ConvertContext == null || ffmpeg.swr_init(ConvertContext) < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"Cannot create sample rate converter for conversion of {af.FramePtr->sample_rate} Hz " +
                           $"{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)af.FramePtr->format)} {af.FramePtr->channels} channels to " +
                           $"{TargetSpec.Frequency} Hz {ffmpeg.av_get_sample_fmt_name(TargetSpec.SampleFormat)} {TargetSpec.Channels} channels!\n");

                    convertContext = ConvertContext;
                    ffmpeg.swr_free(&convertContext);
                    ConvertContext = null;

                    return -1;
                }

                SourceSpec.Layout = dec_channel_layout;
                SourceSpec.Channels = af.FramePtr->channels;
                SourceSpec.Frequency = af.FramePtr->sample_rate;
                SourceSpec.SampleFormat = (AVSampleFormat)af.FramePtr->format;
            }

            if (ConvertContext != null)
            {
                int wantedOutputSize = wantedSampleCount * TargetSpec.Frequency / af.FramePtr->sample_rate + 256;
                int out_size = ffmpeg.av_samples_get_buffer_size(null, TargetSpec.Channels, wantedOutputSize, TargetSpec.SampleFormat, 0);
                int len2;
                if (out_size < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size() failed\n");
                    return -1;
                }
                if (wantedSampleCount != af.FramePtr->nb_samples)
                {
                    if (ffmpeg.swr_set_compensation(ConvertContext, (wantedSampleCount - af.FramePtr->nb_samples) * TargetSpec.Frequency / af.FramePtr->sample_rate,
                                                wantedSampleCount * TargetSpec.Frequency / af.FramePtr->sample_rate) < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_set_compensation() failed\n");
                        return -1;
                    }
                }

                if (Container.audio_buf1 == null)
                {
                    Container.audio_buf1 = (byte*)ffmpeg.av_mallocz((ulong)out_size);
                    Container.audio_buf1_size = (uint)out_size;
                }

                if (Container.audio_buf1_size < out_size && Container.audio_buf1 != null)
                {
                    ffmpeg.av_free(Container.audio_buf1);
                    Container.audio_buf1 = (byte*)ffmpeg.av_mallocz((ulong)out_size);
                    Container.audio_buf1_size = (uint)out_size;
                }

                var audioBufferIn = af.FramePtr->extended_data;
                var audioBufferOut = Container.audio_buf1;
                len2 = ffmpeg.swr_convert(ConvertContext, &audioBufferOut, wantedOutputSize, audioBufferIn, af.FramePtr->nb_samples);
                Container.audio_buf1 = audioBufferOut;
                af.FramePtr->extended_data = audioBufferIn;

                if (len2 < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_convert() failed\n");
                    return -1;
                }
                if (len2 == wantedOutputSize)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, "audio buffer is probably too small\n");
                    if (ffmpeg.swr_init(ConvertContext) < 0)
                    {
                        var convertContext = ConvertContext;
                        ffmpeg.swr_free(&convertContext);
                        ConvertContext = convertContext;
                    }
                }

                Container.audio_buf = Container.audio_buf1;
                resampled_data_size = len2 * TargetSpec.Channels * ffmpeg.av_get_bytes_per_sample(TargetSpec.SampleFormat);
            }
            else
            {
                Container.audio_buf = af.FramePtr->data[0];
                resampled_data_size = data_size;
            }

            audio_clock0 = audio_clock;

            /* update the audio clock with the pts */
            if (!double.IsNaN(af.Pts))
                audio_clock = af.Pts + (double)af.FramePtr->nb_samples / af.FramePtr->sample_rate;
            else
                audio_clock = double.NaN;

            Container.audio_clock_serial = af.Serial;
            if (Debugger.IsAttached)
            {
                Console.WriteLine($"audio: delay={(audio_clock - last_audio_clock),-8:0.####} clock={audio_clock,-8:0.####} clock0={audio_clock0,-8:0.####}");
                last_audio_clock = audio_clock;
            }

            return resampled_data_size;
        }

        /* return the wanted number of samples to get better sync if sync_type is video
* or external master clock */
        public int synchronize_audio(int sampleCount)
        {
            var wantedSampleCount = sampleCount;

            /* if not master, then we try to remove or add samples to correct the clock */
            if (Container.MasterSyncMode != ClockSync.Audio)
            {
                var diff = Container.AudioClock.Time - Container.MasterTime;

                if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD)
                {
                    audio_diff_cum = diff + audio_diff_avg_coef * audio_diff_cum;
                    if (audio_diff_avg_count < Constants.AUDIO_DIFF_AVG_NB)
                    {
                        /* not enough measures to have a correct estimate */
                        audio_diff_avg_count++;
                    }
                    else
                    {
                        /* estimate the A-V difference */
                        var avg_diff = audio_diff_cum * (1.0 - audio_diff_avg_coef);

                        if (Math.Abs(avg_diff) >= audio_diff_threshold)
                        {
                            wantedSampleCount = sampleCount + (int)(diff * SourceSpec.Frequency);
                            var minSampleCount = (int)((sampleCount * (100 - Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            var maxSampleCount = (int)((sampleCount * (100 + Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            wantedSampleCount = Helpers.av_clip(wantedSampleCount, minSampleCount, maxSampleCount);
                        }

                        ffmpeg.av_log(
                            null, ffmpeg.AV_LOG_TRACE, $"diff={diff} adiff={avg_diff} sample_diff={(wantedSampleCount - sampleCount)} apts={audio_clock} {audio_diff_threshold}\n");
                    }
                }
                else
                {
                    /* too big difference : may be initial PTS errors, so
                       reset A-V filter */
                    audio_diff_avg_count = 0;
                    audio_diff_cum = 0;
                }
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
