namespace Unosquare.FFplaySharp.DirectPort
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;

    public static unsafe class PortedProgram
    {


        // TODO: cmdutils.c
        // https://github.com/FFmpeg/FFmpeg/blob/master/fftools/cmdutils.c
        /* current context */

        static long audio_callback_time;
        static long last_mouse_left_click;

        const int FF_QUIT_EVENT = (int)SDL.SDL_EventType.SDL_USEREVENT + 2;

        static MediaContainer GlobalVideoState;
        static MediaRenderer SdlRenderer;

        static bool cmp_audio_fmts(AVSampleFormat fmt1, long channel_count1, AVSampleFormat fmt2, long channel_count2)
        {
            /* If channel count == 1, planar and non-planar formats are the same */
            if (channel_count1 == 1 && channel_count2 == 1)
                return ffmpeg.av_get_packed_sample_fmt(fmt1) != ffmpeg.av_get_packed_sample_fmt(fmt2);
            else
                return channel_count1 != channel_count2 || fmt1 != fmt2;
        }

        static ulong get_valid_channel_layout(ulong channel_layout, int channels)
        {
            if (channel_layout != 0 && ffmpeg.av_get_channel_layout_nb_channels(channel_layout) == channels)
                return channel_layout;
            else
                return 0;
        }

        static void stream_component_close(MediaContainer container, int stream_index)
        {
            AVFormatContext* ic = container.ic;
            AVCodecParameters* codecpar;

            if (stream_index < 0 || stream_index >= ic->nb_streams)
                return;
            codecpar = ic->streams[stream_index]->codecpar;

            switch (codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    container.Audio.Decoder.Abort(container.Audio.Frames);
                    SdlRenderer.CloseAudio();
                    container.Audio.Decoder.Dispose();
                    fixed (SwrContext** swr_ctx = &container.Audio.ConvertContext)
                        ffmpeg.swr_free(swr_ctx);

                    if (container.audio_buf1 != null)
                        ffmpeg.av_free(container.audio_buf1);

                    container.audio_buf1 = null;
                    container.audio_buf1_size = 0;
                    container.audio_buf = null;
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    container.Video.Decoder.Abort(container.Video.Frames);
                    container.Video.Decoder.Dispose();
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    container.Subtitle.Decoder.Abort(container.Subtitle.Frames);
                    container.Subtitle.Decoder.Dispose();
                    break;
                default:
                    break;
            }

            ic->streams[stream_index]->discard = AVDiscard.AVDISCARD_ALL;
            switch (codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    container.Audio.Stream = null;
                    container.Audio.StreamIndex = -1;
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    container.Video.Stream = null;
                    container.Video.StreamIndex = -1;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    container.Subtitle.Stream = null;
                    container.Subtitle.StreamIndex = -1;
                    break;
                default:
                    break;
            }
        }

        static void stream_close(MediaContainer container)
        {
            /* XXX: use a special url_shutdown call to abort parse cleanly */
            container.abort_request = true;
            container.read_tid.Join();

            /* close each stream */
            if (container.Audio.StreamIndex >= 0)
                stream_component_close(container, container.Audio.StreamIndex);

            if (container.Video.StreamIndex >= 0)
                stream_component_close(container, container.Video.StreamIndex);

            if (container.Subtitle.StreamIndex >= 0)
                stream_component_close(container, container.Subtitle.StreamIndex);

            fixed (AVFormatContext** ic = &container.ic)
                ffmpeg.avformat_close_input(ic);

            container.Video.Packets.Dispose();
            container.Audio.Packets.Dispose();
            container.Subtitle.Packets.Dispose();

            /* free all pictures */
            container.Video.Frames?.Dispose();
            container.Audio.Frames?.Dispose();
            container.Subtitle.Frames?.Dispose();

            container.continue_read_thread.Dispose();
            ffmpeg.sws_freeContext(container.Video.ConvertContext);
            ffmpeg.sws_freeContext(container.Subtitle.ConvertContext);

            if (container.vis_texture != IntPtr.Zero)
                SDL.SDL_DestroyTexture(container.vis_texture);

            if (container.vid_texture != IntPtr.Zero)
                SDL.SDL_DestroyTexture(container.vid_texture);
            
            if (container.sub_texture != IntPtr.Zero)
                SDL.SDL_DestroyTexture(container.sub_texture);
        }

        static void do_exit(MediaContainer container)
        {
            if (container != null)
            {
                stream_close(container);
            }

            SdlRenderer.CloseVideo();

            container.Options.uninit_opts();
            container.Options.vfilters_list.Clear();

            ffmpeg.avformat_network_deinit();
            if (container.Options.show_status != 0)
                Console.WriteLine();
            
            SDL.SDL_Quit();
            ffmpeg.av_log(null, ffmpeg.AV_LOG_QUIET, "");
            Environment.Exit(0);
        }

        static void sigterm_handler(int sig)
        {
            Environment.Exit(123);
        }

        /* seek in the stream */
        static void stream_seek(MediaContainer @is, long pos, long rel, int seek_by_bytes)
        {
            if (!@is.seek_req)
            {
                @is.seek_pos = pos;
                @is.seek_rel = rel;
                @is.seek_flags &= ~ffmpeg.AVSEEK_FLAG_BYTE;
                if (seek_by_bytes != 0)
                    @is.seek_flags |= ffmpeg.AVSEEK_FLAG_BYTE;
                @is.seek_req = true;
                @is.continue_read_thread.Set();
            }
        }

        /* pause or resume the video */


        static void toggle_pause(MediaContainer container)
        {
            container.stream_toggle_pause();
            container.step = 0;
        }

        static void toggle_mute(MediaContainer container)
        {
            container.muted = !container.muted;
        }

        static void update_volume(MediaContainer @is, int sign, double step)
        {
            var volume_level = @is.audio_volume > 0 ? (20 * Math.Log(@is.audio_volume / (double)SDL.SDL_MIX_MAXVOLUME) / Math.Log(10)) : -1000.0;
            var new_volume = (int)Math.Round(SDL.SDL_MIX_MAXVOLUME * Math.Pow(10.0, (volume_level + sign * step) / 20.0), 0);
            @is.audio_volume = Helpers.av_clip(@is.audio_volume == new_volume ? (@is.audio_volume + sign) : new_volume, 0, SDL.SDL_MIX_MAXVOLUME);
        }

        static void step_to_next_frame(MediaContainer container)
        {
            /* if the stream is paused unpause it, then step */
            if (container.paused)
                container.stream_toggle_pause();
            container.step = 1;
        }

        static int queue_picture(MediaContainer container, AVFrame* src_frame, double pts, double duration, long pos, int serial)
        {
            var vp = container.Video.Frames.PeekWriteable();

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

            SdlRenderer.set_default_window_size(container, vp.Width, vp.Height, vp.Sar);

            ffmpeg.av_frame_move_ref(vp.FramePtr, src_frame);
            container.Video.Frames.Push();
            return 0;
        }

        static int get_video_frame(MediaContainer container, out AVFrame* frame)
        {
            frame = null;
            int got_picture;

            if ((got_picture = container.Video.Decoder.DecodeFrame(out frame, out _)) < 0)
                return -1;

            if (got_picture != 0)
            {
                double dpts = double.NaN;

                if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    dpts = ffmpeg.av_q2d(container.Video.Stream->time_base) * frame->pts;

                frame->sample_aspect_ratio = ffmpeg.av_guess_sample_aspect_ratio(container.ic, container.Video.Stream, frame);

                if (container.Options.framedrop > 0 || (container.Options.framedrop != 0 && container.MasterSyncMode != ClockSync.Video))
                {
                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        double diff = dpts - container.MasterTime;
                        if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD &&
                            diff - container.frame_last_filter_delay < 0 &&
                            container.Video.Decoder.PacketSerial == container.VideoClock.Serial &&
                            container.Video.Packets.Count != 0)
                        {
                            container.frame_drops_early++;
                            ffmpeg.av_frame_unref(frame);
                            got_picture = 0;
                        }
                    }
                }
            }

            return got_picture;
        }

        static int configure_video_filters(AVFilterGraph* graph, MediaContainer container, string vfilters, AVFrame* frame)
        {
            // enum AVPixelFormat pix_fmts[FF_ARRAY_ELEMS(sdl_texture_format_map)];
            var pix_fmts = new List<int>(MediaRenderer.sdl_texture_map.Count);
            string sws_flags_str = string.Empty;
            string buffersrc_args = string.Empty;
            int ret;
            AVFilterContext* filt_src = null, filt_out = null, last_filter = null;
            AVCodecParameters* codecpar = container.Video.Stream->codecpar;
            AVRational fr = ffmpeg.av_guess_frame_rate(container.ic, container.Video.Stream, null);
            AVDictionaryEntry* e = null;

            for (var i = 0; i < SdlRenderer.renderer_info.num_texture_formats; i++)
            {
                foreach (var kvp in MediaRenderer.sdl_texture_map)
                {
                    if (kvp.Value == SdlRenderer.renderer_info.texture_formats[i])
                    {
                        pix_fmts.Add((int)kvp.Key);
                    }
                }
            }

            //pix_fmts.Add(AVPixelFormat.AV_PIX_FMT_NONE);

            while ((e = ffmpeg.av_dict_get(container.Options.sws_dict, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringUTF8((IntPtr)e->key);
                var value = Marshal.PtrToStringUTF8((IntPtr)e->value);
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

        

        static void audio_thread(object arg)
        {
            var container = arg as MediaContainer;

            FrameHolder af;
            var last_serial = -1;
            long dec_channel_layout;
            bool reconfigure;
            int got_frame = 0;
            AVRational tb;
            int ret = 0;

            var frame = ffmpeg.av_frame_alloc();

            const int bufLength = 1024;
            var buf1 = stackalloc byte[bufLength];
            var buf2 = stackalloc byte[bufLength];

            do
            {
                if ((got_frame = container.Audio.Decoder.DecodeFrame(out frame, out _)) < 0)
                    goto the_end;

                if (got_frame != 0)
                {
                    tb = new() { num = 1, den = frame->sample_rate };

                    dec_channel_layout = (long)get_valid_channel_layout(frame->channel_layout, frame->channels);

                    reconfigure =
                        cmp_audio_fmts(container.Audio.FilterSpec.SampleFormat, container.Audio.FilterSpec.Channels,
                                       (AVSampleFormat)frame->format, frame->channels) ||
                        container.Audio.FilterSpec.Layout != dec_channel_layout ||
                        container.Audio.FilterSpec.Frequency != frame->sample_rate ||
                        container.Audio.Decoder.PacketSerial != last_serial;

                    if (reconfigure)
                    {
                        ffmpeg.av_get_channel_layout_string(buf1, bufLength, -1, (ulong)container.Audio.FilterSpec.Layout);
                        ffmpeg.av_get_channel_layout_string(buf2, bufLength, -1, (ulong)dec_channel_layout);
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Audio frame changed from " +
                           $"rate:{container.Audio.FilterSpec.Frequency} ch:{container.Audio.FilterSpec.Channels} fmt:{ffmpeg.av_get_sample_fmt_name(container.Audio.FilterSpec.SampleFormat)} layout:{Marshal.PtrToStringUTF8((IntPtr)buf1)} serial:{last_serial} to " +
                           $"rate:{frame->sample_rate} ch:{frame->channels} fmt:{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)frame->format)} layout:{Marshal.PtrToStringUTF8((IntPtr)buf2)} serial:{container.Audio.Decoder.PacketSerial}\n");

                        container.Audio.FilterSpec.SampleFormat = (AVSampleFormat)frame->format;
                        container.Audio.FilterSpec.Channels = frame->channels;
                        container.Audio.FilterSpec.Layout = dec_channel_layout;
                        container.Audio.FilterSpec.Frequency = frame->sample_rate;
                        last_serial = container.Audio.Decoder.PacketSerial;

                        if ((ret = container.configure_audio_filters(true)) < 0)
                            goto the_end;
                    }

                    if ((ret = ffmpeg.av_buffersrc_add_frame(container.Audio.InputFilter, frame)) < 0)
                        goto the_end;

                    while ((ret = ffmpeg.av_buffersink_get_frame_flags(container.Audio.OutputFilter, frame, 0)) >= 0)
                    {
                        tb = ffmpeg.av_buffersink_get_time_base(container.Audio.OutputFilter);

                        if ((af = container.Audio.Frames.PeekWriteable()) == null)
                            goto the_end;

                        af.Pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                        af.Position = frame->pkt_pos;
                        af.Serial = container.Audio.Decoder.PacketSerial;
                        af.Duration = ffmpeg.av_q2d(new AVRational() { num = frame->nb_samples, den = frame->sample_rate });

                        ffmpeg.av_frame_move_ref(af.FramePtr, frame);
                        container.Audio.Frames.Push();

                        if (container.Audio.Packets.Serial != container.Audio.Decoder.PacketSerial)
                            break;
                    }
                    if (ret == ffmpeg.AVERROR_EOF)
                        container.Audio.Decoder.HasFinished = container.Audio.Decoder.PacketSerial;
                }
            } while (ret >= 0 || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF);
        the_end:
            fixed (AVFilterGraph** agraph = &container.agraph)
                ffmpeg.avfilter_graph_free(agraph);
            ffmpeg.av_frame_free(&frame);
            // return ret;
        }



        static void video_thread(object arg)
        {
            var container = arg as MediaContainer;
            AVFrame* frame = null; // ffmpeg.av_frame_alloc();
            double pts;
            double duration;
            int ret;
            AVRational tb = container.Video.Stream->time_base;
            AVRational frame_rate = ffmpeg.av_guess_frame_rate(container.ic, container.Video.Stream, null);

            AVFilterGraph* graph = null;
            AVFilterContext* filt_out = null, filt_in = null;
            int last_w = 0;
            int last_h = 0;
            int last_format = -2;
            int last_serial = -1;
            int last_vfilter_idx = 0;

            for (; ; )
            {
                ret = get_video_frame(container, out frame);
                if (ret < 0)
                    goto the_end;

                if (ret == 0)
                    continue;


                if (last_w != frame->width
                    || last_h != frame->height
                    || last_format != frame->format
                    || last_serial != container.Video.Decoder.PacketSerial
                    || last_vfilter_idx != container.vfilter_idx)
                {
                    var lastFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)last_format);
                    lastFormat ??= "none";

                    var frameFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)frame->format);
                    frameFormat ??= "none";

                    ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Video frame changed from size:{last_w}x%{last_h} format:{lastFormat} serial:{last_serial} to " +
                           $"size:{frame->width}x{frame->height} format:{frameFormat} serial:{container.Video.Decoder.PacketSerial}\n");

                    ffmpeg.avfilter_graph_free(&graph);
                    graph = ffmpeg.avfilter_graph_alloc();
                    if (graph == null)
                    {
                        ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                        goto the_end;
                    }
                    graph->nb_threads = container.Options.filter_nbthreads;
                    if ((ret = configure_video_filters(graph, container, container.Options.vfilters_list.Count > 0
                        ? container.Options.vfilters_list[container.vfilter_idx]
                        : null, frame)) < 0)
                    {
                        var evt = new SDL.SDL_Event()
                        {
                            type = (SDL.SDL_EventType)FF_QUIT_EVENT,
                        };

                        // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                        SDL.SDL_PushEvent(ref evt);
                        goto the_end;
                    }

                    filt_in = container.Video.InputFilter;
                    filt_out = container.Video.OutputFilter;
                    last_w = frame->width;
                    last_h = frame->height;
                    last_format = frame->format;
                    last_serial = container.Video.Decoder.PacketSerial;
                    last_vfilter_idx = container.vfilter_idx;
                    frame_rate = ffmpeg.av_buffersink_get_frame_rate(filt_out);
                }

                ret = ffmpeg.av_buffersrc_add_frame(filt_in, frame);
                if (ret < 0)
                    goto the_end;

                while (ret >= 0)
                {
                    container.frame_last_returned_time = ffmpeg.av_gettime_relative() / 1000000.0;

                    ret = ffmpeg.av_buffersink_get_frame_flags(filt_out, frame, 0);
                    if (ret < 0)
                    {
                        if (ret == ffmpeg.AVERROR_EOF)
                            container.Video.Decoder.HasFinished = container.Video.Decoder.PacketSerial;
                        ret = 0;
                        break;
                    }

                    container.frame_last_filter_delay = ffmpeg.av_gettime_relative() / 1000000.0 - container.frame_last_returned_time;
                    if (Math.Abs(container.frame_last_filter_delay) > Constants.AV_NOSYNC_THRESHOLD / 10.0)
                        container.frame_last_filter_delay = 0;

                    tb = ffmpeg.av_buffersink_get_time_base(filt_out);
                    duration = (frame_rate.num != 0 && frame_rate.den != 0 ? ffmpeg.av_q2d(new AVRational() { num = frame_rate.den, den = frame_rate.num }) : 0);
                    pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                    ret = queue_picture(container, frame, pts, duration, frame->pkt_pos, container.Video.Decoder.PacketSerial);
                    ffmpeg.av_frame_unref(frame);

                    if (container.Video.Packets.Serial != container.Video.Decoder.PacketSerial)
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

        static void subtitle_thread(object arg)
        {
            var @is = arg as MediaContainer;
            FrameHolder sp;
            int got_subtitle;
            double pts;

            for (; ; )
            {
                if ((sp = @is.Subtitle.Frames.PeekWriteable()) == null)
                    return; // 0;

                if ((got_subtitle = @is.Subtitle.Decoder.DecodeFrame(out _, out var spsub)) < 0)
                    break;
                else
                    sp.SubtitlePtr = spsub;

                pts = 0;

                if (got_subtitle != 0 && sp.SubtitlePtr->format == 0)
                {
                    if (sp.SubtitlePtr->pts != ffmpeg.AV_NOPTS_VALUE)
                        pts = sp.SubtitlePtr->pts / (double)ffmpeg.AV_TIME_BASE;
                    sp.Pts = pts;
                    sp.Serial = @is.Subtitle.Decoder.PacketSerial;
                    sp.Width = @is.Subtitle.Decoder.CodecContext->width;
                    sp.Height = @is.Subtitle.Decoder.CodecContext->height;
                    sp.uploaded = false;

                    /* now we can update the picture count */
                    @is.Subtitle.Frames.Push();
                }
                else if (got_subtitle != 0)
                {
                    ffmpeg.avsubtitle_free(sp.SubtitlePtr);
                }
            }
            return; // 0
        }

        /* copy samples for viewing in editor window */
        static void update_sample_display(MediaContainer @is, short* samples, int samples_size)
        {
            int size, len;

            size = samples_size / sizeof(short);
            while (size > 0)
            {
                len = Constants.SAMPLE_ARRAY_SIZE - @is.sample_array_index;
                if (len > size)
                    len = size;

                fixed (short* targetAddress = &@is.sample_array[@is.sample_array_index])
                    Buffer.MemoryCopy(samples, targetAddress, len * sizeof(short), len * sizeof(short));

                samples += len;
                @is.sample_array_index += len;
                if (@is.sample_array_index >= Constants.SAMPLE_ARRAY_SIZE)
                    @is.sample_array_index = 0;
                size -= len;
            }
        }

        /* return the wanted number of samples to get better sync if sync_type is video
 * or external master clock */
        static int synchronize_audio(MediaContainer container, int sampleCount)
        {
            var wantedSampleCount = sampleCount;

            /* if not master, then we try to remove or add samples to correct the clock */
            if (container.MasterSyncMode != ClockSync.Audio)
            {
                double diff, avg_diff;
                int min_nb_samples, max_nb_samples;

                diff = container.AudioClock.Time - container.MasterTime;

                if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD)
                {
                    container.audio_diff_cum = diff + container.audio_diff_avg_coef * container.audio_diff_cum;
                    if (container.audio_diff_avg_count < Constants.AUDIO_DIFF_AVG_NB)
                    {
                        /* not enough measures to have a correct estimate */
                        container.audio_diff_avg_count++;
                    }
                    else
                    {
                        /* estimate the A-V difference */
                        avg_diff = container.audio_diff_cum * (1.0 - container.audio_diff_avg_coef);

                        if (Math.Abs(avg_diff) >= container.audio_diff_threshold)
                        {
                            wantedSampleCount = sampleCount + (int)(diff * container.Audio.SourceSpec.Frequency);
                            min_nb_samples = (int)((sampleCount * (100 - Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            max_nb_samples = (int)((sampleCount * (100 + Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            wantedSampleCount = Helpers.av_clip(wantedSampleCount, min_nb_samples, max_nb_samples);
                        }

                        ffmpeg.av_log(
                            null, ffmpeg.AV_LOG_TRACE, $"diff={diff} adiff={avg_diff} sample_diff={(wantedSampleCount - sampleCount)} apts={container.audio_clock} {container.audio_diff_threshold}\n");
                    }
                }
                else
                {
                    /* too big difference : may be initial PTS errors, so
                       reset A-V filter */
                    container.audio_diff_avg_count = 0;
                    container.audio_diff_cum = 0;
                }
            }

            return wantedSampleCount;
        }

        /**
 * Decode one audio frame and return its uncompressed size.
 *
 * The processed audio frame is decoded, converted if required, and
 * stored in is->audio_buf, with size in bytes given by the return
 * value.
 */
        static int audio_decode_frame(MediaContainer container)
        {
            int data_size, resampled_data_size;
            long dec_channel_layout;
            double audio_clock0;
            int wanted_nb_samples;
            FrameHolder af;

            if (container.paused)
                return -1;

            do
            {
                while (container.Audio.Frames.PendingCount == 0)
                {
                    if ((ffmpeg.av_gettime_relative() - audio_callback_time) > 1000000L * container.audio_hw_buf_size / container.Audio.TargetSpec.BytesPerSecond / 2)
                        return -1;
                    ffmpeg.av_usleep(1000);
                }

                if ((af = container.Audio.Frames.PeekReadable()) == null)
                    return -1;

                container.Audio.Frames.Next();

            } while (af.Serial != container.Audio.Packets.Serial);

            data_size = ffmpeg.av_samples_get_buffer_size(null, af.FramePtr->channels,
                                                   af.FramePtr->nb_samples,
                                                   (AVSampleFormat)af.FramePtr->format, 1);

            dec_channel_layout =
                (af.FramePtr->channel_layout != 0 && af.FramePtr->channels == ffmpeg.av_get_channel_layout_nb_channels(af.FramePtr->channel_layout))
                ? (long)af.FramePtr->channel_layout
                : ffmpeg.av_get_default_channel_layout(af.FramePtr->channels);
            wanted_nb_samples = synchronize_audio(container, af.FramePtr->nb_samples);

            if (af.FramePtr->format != (int)container.Audio.SourceSpec.SampleFormat ||
                dec_channel_layout != container.Audio.SourceSpec.Layout ||
                af.FramePtr->sample_rate != container.Audio.SourceSpec.Frequency ||
                (wanted_nb_samples != af.FramePtr->nb_samples && container.Audio.ConvertContext == null))
            {
                fixed (SwrContext** is_swr_ctx = &container.Audio.ConvertContext)
                    ffmpeg.swr_free(is_swr_ctx);

                container.Audio.ConvertContext = ffmpeg.swr_alloc_set_opts(null,
                                                 container.Audio.TargetSpec.Layout, container.Audio.TargetSpec.SampleFormat, container.Audio.TargetSpec.Frequency,
                                                 dec_channel_layout, (AVSampleFormat)af.FramePtr->format, af.FramePtr->sample_rate,
                                                 0, null);

                if (container.Audio.ConvertContext == null || ffmpeg.swr_init(container.Audio.ConvertContext) < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"Cannot create sample rate converter for conversion of {af.FramePtr->sample_rate} Hz " +
                           $"{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)af.FramePtr->format)} {af.FramePtr->channels} channels to " +
                           $"{container.Audio.TargetSpec.Frequency} Hz {ffmpeg.av_get_sample_fmt_name(container.Audio.TargetSpec.SampleFormat)} {container.Audio.TargetSpec.Channels} channels!\n");

                    fixed (SwrContext** is_swr_ctx = &container.Audio.ConvertContext)
                        ffmpeg.swr_free(is_swr_ctx);

                    return -1;
                }
                container.Audio.SourceSpec.Layout = dec_channel_layout;
                container.Audio.SourceSpec.Channels = af.FramePtr->channels;
                container.Audio.SourceSpec.Frequency = af.FramePtr->sample_rate;
                container.Audio.SourceSpec.SampleFormat = (AVSampleFormat)af.FramePtr->format;
            }

            if (container.Audio.ConvertContext != null)
            {
                var @in = af.FramePtr->extended_data;

                int out_count = (int)((long)wanted_nb_samples * container.Audio.TargetSpec.Frequency / af.FramePtr->sample_rate + 256);
                int out_size = ffmpeg.av_samples_get_buffer_size(null, container.Audio.TargetSpec.Channels, out_count, container.Audio.TargetSpec.SampleFormat, 0);
                int len2;
                if (out_size < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size() failed\n");
                    return -1;
                }
                if (wanted_nb_samples != af.FramePtr->nb_samples)
                {
                    if (ffmpeg.swr_set_compensation(container.Audio.ConvertContext, (wanted_nb_samples - af.FramePtr->nb_samples) * container.Audio.TargetSpec.Frequency / af.FramePtr->sample_rate,
                                                wanted_nb_samples * container.Audio.TargetSpec.Frequency / af.FramePtr->sample_rate) < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_set_compensation() failed\n");
                        return -1;
                    }
                }

                if (container.audio_buf1 == null)
                {
                    container.audio_buf1 = (byte*)ffmpeg.av_mallocz((ulong)out_size);
                    container.audio_buf1_size = (uint)out_size;
                }

                if (container.audio_buf1_size < out_size && container.audio_buf1 != null)
                {
                    ffmpeg.av_free(container.audio_buf1);
                    container.audio_buf1 = (byte*)ffmpeg.av_mallocz((ulong)out_size);
                    container.audio_buf1_size = (uint)out_size;
                }

                fixed (byte** @out = &container.audio_buf1)
                    len2 = ffmpeg.swr_convert(container.Audio.ConvertContext, @out, out_count, @in, af.FramePtr->nb_samples);

                if (len2 < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_convert() failed\n");
                    return -1;
                }
                if (len2 == out_count)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, "audio buffer is probably too small\n");
                    if (ffmpeg.swr_init(container.Audio.ConvertContext) < 0)
                    {
                        fixed (SwrContext** is_swr_ctx = &container.Audio.ConvertContext)
                            ffmpeg.swr_free(is_swr_ctx);
                    }
                }

                container.audio_buf = container.audio_buf1;
                resampled_data_size = len2 * container.Audio.TargetSpec.Channels * ffmpeg.av_get_bytes_per_sample(container.Audio.TargetSpec.SampleFormat);
            }
            else
            {
                container.audio_buf = af.FramePtr->data[0];
                resampled_data_size = data_size;
            }

            audio_clock0 = container.audio_clock;

            /* update the audio clock with the pts */
            if (!double.IsNaN(af.Pts))
                container.audio_clock = af.Pts + (double)af.FramePtr->nb_samples / af.FramePtr->sample_rate;
            else
                container.audio_clock = double.NaN;

            container.audio_clock_serial = af.Serial;
            if (Debugger.IsAttached)
            {
                Console.WriteLine($"audio: delay={(container.audio_clock - container.Options.last_audio_clock),-8:0.####} clock={container.audio_clock,-8:0.####} clock0={audio_clock0,-8:0.####}");
                container.Options.last_audio_clock = container.audio_clock;
            }

            return resampled_data_size;
        }

        /* prepare a new audio buffer */
        static void sdl_audio_callback(IntPtr opaque, IntPtr stream, int len)
        {
            var container = GlobalVideoState;
            int audio_size, len1;

            audio_callback_time = ffmpeg.av_gettime_relative();

            while (len > 0)
            {
                if (container.audio_buf_index >= container.audio_buf_size)
                {
                    audio_size = audio_decode_frame(container);
                    if (audio_size < 0)
                    {
                        /* if error, just output silence */
                        container.audio_buf = null;
                        container.audio_buf_size = (uint)(Constants.SDL_AUDIO_MIN_BUFFER_SIZE / container.Audio.TargetSpec.FrameSize * container.Audio.TargetSpec.FrameSize);
                    }
                    else
                    {
                        if (container.show_mode != ShowMode.Video)
                            update_sample_display(container, (short*)container.audio_buf, audio_size);
                        container.audio_buf_size = (uint)audio_size;
                    }
                    container.audio_buf_index = 0;
                }
                len1 = (int)(container.audio_buf_size - container.audio_buf_index);
                if (len1 > len)
                    len1 = len;

                if (!container.muted && container.audio_buf != null && container.audio_volume == SDL.SDL_MIX_MAXVOLUME)
                {
                    var dest = (byte*)stream;
                    var source = (container.audio_buf + container.audio_buf_index);
                    for (var b = 0; b < len1; b++)
                        dest[b] = source[b];
                }
                else
                {
                    var target = (byte*)stream;
                    for (var b = 0; b < len1; b++)
                        target[b] = 0;

                    if (!container.muted && container.audio_buf != null)
                        SDLNatives.SDL_MixAudioFormat((byte*)stream, container.audio_buf + container.audio_buf_index, SDL.AUDIO_S16SYS, (uint)len1, container.audio_volume);
                }

                len -= len1;
                stream += len1;
                container.audio_buf_index += len1;
            }
            container.audio_write_buf_size = (int)(container.audio_buf_size - container.audio_buf_index);
            /* Let's assume the audio driver that is used by SDL has two periods. */
            if (!double.IsNaN(container.audio_clock))
            {
                container.AudioClock.Set(container.audio_clock - (double)(2 * container.audio_hw_buf_size + container.audio_write_buf_size) / container.Audio.TargetSpec.BytesPerSecond, container.audio_clock_serial, audio_callback_time / 1000000.0);
                container.ExternalClock.SyncToSlave(container.AudioClock);
            }
        }

        static int audio_open(MediaContainer container, long wanted_channel_layout, int wanted_nb_channels, int wanted_sample_rate, ref AudioParams audio_hw_params)
        {
            SDL.SDL_AudioSpec wanted_spec = new();
            SDL.SDL_AudioSpec spec = new();

            var next_nb_channels = new[] { 0, 0, 1, 6, 2, 6, 4, 6 };
            var next_sample_rates = new[] { 0, 44100, 48000, 96000, 192000 };
            int next_sample_rate_idx = next_sample_rates.Length - 1;

            var env = Environment.GetEnvironmentVariable("SDL_AUDIO_CHANNELS");
            if (!string.IsNullOrWhiteSpace(env))
            {
                wanted_nb_channels = int.Parse(env);
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_nb_channels);
            }

            if (wanted_channel_layout == 0 || wanted_nb_channels != ffmpeg.av_get_channel_layout_nb_channels((ulong)wanted_channel_layout))
            {
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_nb_channels);
                wanted_channel_layout &= ~ffmpeg.AV_CH_LAYOUT_STEREO_DOWNMIX;
            }

            wanted_nb_channels = ffmpeg.av_get_channel_layout_nb_channels((ulong)wanted_channel_layout);
            wanted_spec.channels = (byte)wanted_nb_channels;
            wanted_spec.freq = wanted_sample_rate;
            if (wanted_spec.freq <= 0 || wanted_spec.channels <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Invalid sample rate or channel count!\n");
                return -1;
            }

            while (next_sample_rate_idx != 0 && next_sample_rates[next_sample_rate_idx] >= wanted_spec.freq)
                next_sample_rate_idx--;

            wanted_spec.format = SDL.AUDIO_S16SYS;
            wanted_spec.silence = 0;
            wanted_spec.samples = (ushort)Math.Max(Constants.SDL_AUDIO_MIN_BUFFER_SIZE, 2 << ffmpeg.av_log2((uint)(wanted_spec.freq / Constants.SDL_AUDIO_MAX_CALLBACKS_PER_SEC)));
            wanted_spec.callback = sdl_audio_callback;
            // wanted_spec.userdata = GCHandle.ToIntPtr(VideoStateHandle);
            while ((SdlRenderer.audio_dev = SDL.SDL_OpenAudioDevice(null, 0, ref wanted_spec, out spec, (int)(SDL.SDL_AUDIO_ALLOW_FREQUENCY_CHANGE | SDL.SDL_AUDIO_ALLOW_CHANNELS_CHANGE))) == 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"SDL_OpenAudio ({wanted_spec.channels} channels, {wanted_spec.freq} Hz): {SDL.SDL_GetError()}\n");
                wanted_spec.channels = (byte)next_nb_channels[Math.Min(7, (int)wanted_spec.channels)];
                if (wanted_spec.channels == 0)
                {
                    wanted_spec.freq = next_sample_rates[next_sample_rate_idx--];
                    wanted_spec.channels = (byte)wanted_nb_channels;
                    if (wanted_spec.freq == 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "No more combinations to try, audio open failed\n");
                        return -1;
                    }
                }

                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_spec.channels);
            }
            if (spec.format != SDL.AUDIO_S16SYS)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                       $"SDL advised audio format {spec.format} is not supported!\n");
                return -1;
            }
            if (spec.channels != wanted_spec.channels)
            {
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(spec.channels);
                if (wanted_channel_layout == 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"SDL advised channel count {spec.channels} is not supported!\n");
                    return -1;
                }
            }

            audio_hw_params.SampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            audio_hw_params.Frequency = spec.freq;
            audio_hw_params.Layout = wanted_channel_layout;
            audio_hw_params.Channels = spec.channels;
            audio_hw_params.FrameSize = ffmpeg.av_samples_get_buffer_size(null, audio_hw_params.Channels, 1, audio_hw_params.SampleFormat, 1);
            audio_hw_params.BytesPerSecond = ffmpeg.av_samples_get_buffer_size(null, audio_hw_params.Channels, audio_hw_params.Frequency, audio_hw_params.SampleFormat, 1);
            if (audio_hw_params.BytesPerSecond <= 0 || audio_hw_params.FrameSize <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size failed\n");
                return -1;
            }

            return (int)spec.size;
        }

        /* open a given stream. Return 0 if OK */
        static int stream_component_open(MediaContainer container, int stream_index)
        {
            AVFormatContext* ic = container.ic;
            AVCodecContext* avctx;
            AVCodec* codec;
            string forcedCodecName = null;
            AVDictionary* opts = null;
            AVDictionaryEntry* t = null;
            int sampleRate, nb_channels;
            long channelLayout;
            int ret = 0;
            int stream_lowres = container.Options.lowres;

            if (stream_index < 0 || stream_index >= ic->nb_streams)
                return -1;

            avctx = ffmpeg.avcodec_alloc_context3(null);
            if (avctx == null)
                return ffmpeg.AVERROR(ffmpeg.ENOMEM);

            ret = ffmpeg.avcodec_parameters_to_context(avctx, ic->streams[stream_index]->codecpar);
            if (ret < 0) goto fail;
            avctx->pkt_timebase = ic->streams[stream_index]->time_base;

            codec = ffmpeg.avcodec_find_decoder(avctx->codec_id);

            switch (avctx->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO: container.Audio.LastStreamIndex = stream_index; forcedCodecName = container.Options.AudioForcedCodecName; break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE: container.Subtitle.LastStreamIndex = stream_index; forcedCodecName = container.Options.SubtitleForcedCodecName; break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO: container.Video.LastStreamIndex = stream_index; forcedCodecName = container.Options.VideoForcedCodecName; break;
            }
            if (!string.IsNullOrWhiteSpace(forcedCodecName))
                codec = ffmpeg.avcodec_find_decoder_by_name(forcedCodecName);

            if (codec == null)
            {
                if (!string.IsNullOrWhiteSpace(forcedCodecName))
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"No codec could be found with name '{forcedCodecName}'\n");
                else
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"No decoder could be found for codec {ffmpeg.avcodec_get_name(avctx->codec_id)}\n");
                ret = ffmpeg.AVERROR(ffmpeg.EINVAL);
                goto fail;
            }

            avctx->codec_id = codec->id;
            if (stream_lowres > codec->max_lowres)
            {
                ffmpeg.av_log(avctx, ffmpeg.AV_LOG_WARNING, $"The maximum value for lowres supported by the decoder is {codec->max_lowres}\n");
                stream_lowres = codec->max_lowres;
            }

            avctx->lowres = stream_lowres;

            if (container.Options.fast != 0)
                avctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            opts = Helpers.filter_codec_opts(container.Options.codec_opts, avctx->codec_id, ic, ic->streams[stream_index], codec);
            if (ffmpeg.av_dict_get(opts, "threads", null, 0) == null)
                ffmpeg.av_dict_set(&opts, "threads", "auto", 0);

            if (stream_lowres != 0)
                ffmpeg.av_dict_set_int(&opts, "lowres", stream_lowres, 0);

            if (avctx->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO || avctx->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                ffmpeg.av_dict_set(&opts, "refcounted_frames", "1", 0);

            if ((ret = ffmpeg.avcodec_open2(avctx, codec, &opts)) < 0)
            {
                goto fail;
            }
            if ((t = ffmpeg.av_dict_get(opts, "", null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringUTF8((IntPtr)t->key);
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {key} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            container.eof = false;
            ic->streams[stream_index]->discard = AVDiscard.AVDISCARD_DEFAULT;
            switch (avctx->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    {
                        AVFilterContext* sink;
                        container.Audio.FilterSpec.Frequency = avctx->sample_rate;
                        container.Audio.FilterSpec.Channels = avctx->channels;
                        container.Audio.FilterSpec.Layout = (long)get_valid_channel_layout(avctx->channel_layout, avctx->channels);
                        container.Audio.FilterSpec.SampleFormat = avctx->sample_fmt;
                        if ((ret = container.configure_audio_filters(false)) < 0)
                            goto fail;
                        sink = container.Audio.OutputFilter;
                        sampleRate = ffmpeg.av_buffersink_get_sample_rate(sink);
                        nb_channels = ffmpeg.av_buffersink_get_channels(sink);
                        channelLayout = (long)ffmpeg.av_buffersink_get_channel_layout(sink);
                    }

                    sampleRate = avctx->sample_rate;
                    nb_channels = avctx->channels;
                    channelLayout = (long)avctx->channel_layout;

                    /* prepare audio output */
                    if ((ret = audio_open(container, channelLayout, nb_channels, sampleRate, ref container.Audio.TargetSpec)) < 0)
                        goto fail;

                    container.audio_hw_buf_size = ret;
                    container.Audio.SourceSpec = container.Audio.TargetSpec;
                    container.audio_buf_size = 0;
                    container.audio_buf_index = 0;

                    /* init averaging filter */
                    container.audio_diff_avg_coef = Math.Exp(Math.Log(0.01) / Constants.AUDIO_DIFF_AVG_NB);
                    container.audio_diff_avg_count = 0;

                    /* since we do not have a precise anough audio FIFO fullness,
                       we correct audio sync only if larger than this threshold */
                    container.audio_diff_threshold = (double)(container.audio_hw_buf_size) / container.Audio.TargetSpec.BytesPerSecond;

                    container.Audio.StreamIndex = stream_index;
                    container.Audio.Stream = ic->streams[stream_index];

                    container.Audio.Decoder = new(container.Audio, avctx);
                    if ((container.ic->iformat->flags & (ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK)) != 0 &&
                        container.ic->iformat->read_seek.Pointer == IntPtr.Zero)
                    {
                        container.Audio.Decoder.StartPts = container.Audio.Stream->start_time;
                        container.Audio.Decoder.StartPtsTimeBase = container.Audio.Stream->time_base;
                    }

                    if ((ret = container.Audio.Decoder.Start(audio_thread, "audio_decoder", container)) < 0)
                        goto @out;
                    SdlRenderer.PauseAudio();
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    container.Video.StreamIndex = stream_index;
                    container.Video.Stream = ic->streams[stream_index];

                    container.Video.Decoder = new(container.Video, avctx);
                    if ((ret = container.Video.Decoder.Start(video_thread, "video_decoder", container)) < 0)
                        goto @out;
                    container.queue_attachments_req = true;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    container.Subtitle.StreamIndex = stream_index;
                    container.Subtitle.Stream = ic->streams[stream_index];

                    container.Subtitle.Decoder = new(container.Subtitle, avctx); ;
                    if ((ret = container.Subtitle.Decoder.Start(subtitle_thread, "subtitle_decoder", container)) < 0)
                        goto @out;
                    break;
                default:
                    break;
            }
            goto @out;

        fail:
            ffmpeg.avcodec_free_context(&avctx);
        @out:
            ffmpeg.av_dict_free(&opts);

            return ret;
        }

        static int decode_interrupt_cb(void* ctx)
        {
            var container = GlobalVideoState;
            return container.abort_request ? 1 : 0;
        }



        static bool is_realtime(AVFormatContext* ic)
        {
            var iformat = Helpers.PtrToString(ic->iformat->name);
            if (iformat == "rtp" || iformat == "rtsp" || iformat == "sdp")
                return true;

            var url = Helpers.PtrToString(ic->url);
            url = string.IsNullOrEmpty(url) ? string.Empty : url;

            if (ic->pb != null && (url.StartsWith("rtp:") || url.StartsWith("udp:")))
                return true;

            return false;
        }

        /* this thread gets the stream from the disk or the network */
        static void read_thread(object arg)
        {
            var container = arg as MediaContainer;
            var o = container.Options;
            AVFormatContext * ic = null;
            int err, i, ret;
            var st_index = new int[(int)AVMediaType.AVMEDIA_TYPE_NB];
            // AVPacket pkt1;
            // AVPacket* pkt = &pkt1;
            long stream_start_time;
            bool pkt_in_play_range = false;
            AVDictionaryEntry* t;
            bool scan_all_pmts_set = false;
            long pkt_ts;

            for (var b = 0; b < st_index.Length; b++)
                st_index[b] = -1;

            container.eof = false;

            ic = ffmpeg.avformat_alloc_context();
            if (ic == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Could not allocate context.\n");
                ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                goto fail;
            }

            ic->interrupt_callback.callback = (AVIOInterruptCB_callback)decode_interrupt_cb;
            // ic->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(VideoStateHandle);
            if (ffmpeg.av_dict_get(o.format_opts, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE) == null)
            {
                fixed (AVDictionary** ref_format_opts = &o.format_opts)
                    ffmpeg.av_dict_set(ref_format_opts, "scan_all_pmts", "1", ffmpeg.AV_DICT_DONT_OVERWRITE);
                scan_all_pmts_set = true;
            }

            fixed (AVDictionary** ref_format_opts = &o.format_opts)
                err = ffmpeg.avformat_open_input(&ic, container.filename, container.iformat, ref_format_opts);

            if (err < 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"{container.filename}: {Helpers.print_error(err)}\n");
                ret = -1;
                goto fail;
            }
            if (scan_all_pmts_set)
            {
                fixed (AVDictionary** ref_format_opts = &o.format_opts)
                    ffmpeg.av_dict_set(ref_format_opts, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE);
            }

            if ((t = ffmpeg.av_dict_get(o.format_opts, "", null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {Helpers.PtrToString(t->key)} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            container.ic = ic;

            if (o.genpts)
                ic->flags |= ffmpeg.AVFMT_FLAG_GENPTS;

            ffmpeg.av_format_inject_global_side_data(ic);

            if (o.find_stream_info)
            {
                AVDictionary** opts = Helpers.setup_find_stream_info_opts(ic, o.codec_opts);
                int orig_nb_streams = (int)ic->nb_streams;

                err = ffmpeg.avformat_find_stream_info(ic, opts);

                for (i = 0; i < orig_nb_streams; i++)
                    ffmpeg.av_dict_free(&opts[i]);

                ffmpeg.av_freep(&opts);

                if (err < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"{container.filename}: could not find codec parameters\n");
                    ret = -1;
                    goto fail;
                }
            }

            if (ic->pb != null)
                ic->pb->eof_reached = 0; // FIXME hack, ffplay maybe should not use avio_feof() to test for the end

            if (o.seek_by_bytes < 0)
                o.seek_by_bytes = ((ic->iformat->flags & ffmpeg.AVFMT_TS_DISCONT) > 0 && Helpers.PtrToString(ic->iformat->name) != "ogg") ? 1 : 0;

            container.max_frame_duration = (ic->iformat->flags & ffmpeg.AVFMT_TS_DISCONT) != 0 ? 10.0 : 3600.0;

            if (string.IsNullOrWhiteSpace(SdlRenderer.window_title) && (t = ffmpeg.av_dict_get(ic->metadata, "title", null, 0)) != null)
                SdlRenderer.window_title = $"{Helpers.PtrToString(t->value)} - {o.input_filename}";

            /* if seeking requested, we execute it */
            if (o.start_time != ffmpeg.AV_NOPTS_VALUE)
            {
                long timestamp;

                timestamp = o.start_time;
                /* add the stream start time */
                if (ic->start_time != ffmpeg.AV_NOPTS_VALUE)
                    timestamp += ic->start_time;
                ret = ffmpeg.avformat_seek_file(ic, -1, long.MinValue, timestamp, long.MaxValue, 0);
                if (ret < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"{container.filename}: could not seek to position {((double)timestamp / ffmpeg.AV_TIME_BASE)}\n");
                }
            }

            container.realtime = is_realtime(ic);

            if (o.show_status != 0)
                ffmpeg.av_dump_format(ic, 0, container.filename, 0);

            for (i = 0; i < ic->nb_streams; i++)
            {
                AVStream* st = ic->streams[i];
                var type = (int)st->codecpar->codec_type;
                st->discard = AVDiscard.AVDISCARD_ALL;
                if (type >= 0 && o.wanted_stream_spec[type] != null && st_index[type] == -1)
                    if (ffmpeg.avformat_match_stream_specifier(ic, st, o.wanted_stream_spec[type]) > 0)
                        st_index[type] = i;
            }
            for (i = 0; i < (int)AVMediaType.AVMEDIA_TYPE_NB; i++)
            {
                if (o.wanted_stream_spec[i] != null && st_index[i] == -1)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Stream specifier {container.Options.wanted_stream_spec[i]} does not match any {ffmpeg.av_get_media_type_string((AVMediaType)i)} stream\n");
                    st_index[i] = int.MaxValue;
                }
            }

            if (!o.video_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_VIDEO,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO], -1, null, 0);
            if (!o.audio_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_AUDIO,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO],
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO],
                                        null, 0);
            if (!o.video_disable && !o.subtitle_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_SUBTITLE,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE],
                                        (st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0 ?
                                         st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] :
                                         st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]),
                                        null, 0);

            container.show_mode = o.show_mode;
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                AVStream* st = ic->streams[st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]];
                AVCodecParameters* codecpar = st->codecpar;
                AVRational sar = ffmpeg.av_guess_sample_aspect_ratio(ic, st, null);
                if (codecpar->width != 0)
                    SdlRenderer.set_default_window_size(container, codecpar->width, codecpar->height, sar);
            }

            /* open the streams */
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0)
            {
                stream_component_open(container, st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO]);
            }

            ret = -1;
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                ret = stream_component_open(container, st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]);
            }

            if (container.show_mode == ShowMode.None)
                container.show_mode = ret >= 0 ? ShowMode.Video : ShowMode.Rdft;

            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE] >= 0)
            {
                stream_component_open(container, st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE]);
            }

            if (container.Video.StreamIndex < 0 && container.Audio.StreamIndex < 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Failed to open file '{container.filename}' or configure filtergraph\n");
                ret = -1;
                goto fail;
            }

            if (o.infinite_buffer < 0 && container.realtime)
                o.infinite_buffer = 1;

            while (true)
            {
                if (container.abort_request)
                    break;
                if (container.paused != container.last_paused)
                {
                    container.last_paused = container.paused;
                    if (container.paused)
                        container.read_pause_return = ffmpeg.av_read_pause(ic);
                    else
                        ffmpeg.av_read_play(ic);
                }

                if (container.paused &&
                        (Helpers.PtrToString(ic->iformat->name) == "rtsp" ||
                         (ic->pb != null && o.input_filename.StartsWith("mmsh:"))))
                {
                    /* wait 10 ms to avoid trying to get another packet */
                    /* XXX: horrible */
                    SDL.SDL_Delay(10);
                    continue;
                }

                if (container.seek_req)
                {
                    long seek_target = container.seek_pos;
                    long seek_min = container.seek_rel > 0 ? seek_target - container.seek_rel + 2 : long.MinValue;
                    long seek_max = container.seek_rel < 0 ? seek_target - container.seek_rel - 2 : long.MaxValue;
                    // FIXME the +-2 is due to rounding being not done in the correct direction in generation
                    //      of the seek_pos/seek_rel variables

                    ret = ffmpeg.avformat_seek_file(container.ic, -1, seek_min, seek_target, seek_max, container.seek_flags);
                    if (ret < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"{Helpers.PtrToString(container.ic->url)}: error while seeking\n");
                    }
                    else
                    {
                        if (container.Audio.StreamIndex >= 0)
                        {
                            container.Audio.Packets.Clear();
                            container.Audio.Packets.PutFlush();
                        }
                        if (container.Subtitle.StreamIndex >= 0)
                        {
                            container.Subtitle.Packets.Clear();
                            container.Subtitle.Packets.PutFlush();
                        }
                        if (container.Video.StreamIndex >= 0)
                        {
                            container.Video.Packets.Clear();
                            container.Video.Packets.PutFlush();
                        }
                        if ((container.seek_flags & ffmpeg.AVSEEK_FLAG_BYTE) != 0)
                        {
                            container.ExternalClock.Set(double.NaN, 0);
                        }
                        else
                        {
                            container.ExternalClock.Set(seek_target / (double)ffmpeg.AV_TIME_BASE, 0);
                        }
                    }
                    container.seek_req = false;
                    container.queue_attachments_req = true;
                    container.eof = false;

                    if (container.paused)
                        step_to_next_frame(container);
                }
                if (container.queue_attachments_req)
                {
                    if (container.Video.Stream != null && (container.Video.Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0)
                    {
                        var copy = ffmpeg.av_packet_clone(&container.Video.Stream->attached_pic);
                        container.Video.Packets.Put(copy);
                        container.Video.Packets.PutNull(container.Video.StreamIndex);
                    }

                    container.queue_attachments_req = false;
                }

                /* if the queue are full, no need to read more */
                if (o.infinite_buffer < 1 &&
                      (container.Audio.Packets.Size + container.Video.Packets.Size + container.Subtitle.Packets.Size > Constants.MAX_QUEUE_SIZE
                    || (container.Audio.HasEnoughPackets &&
                        container.Video.HasEnoughPackets &&
                        container.Subtitle.HasEnoughPackets)))
                {
                    /* wait 10 ms */
                    container.continue_read_thread.WaitOne(10);
                    continue;
                }
                if (!container.paused &&
                    (container.Audio.Stream == null || (container.Audio.Decoder.HasFinished == container.Audio.Packets.Serial && container.Audio.Frames.PendingCount == 0)) &&
                    (container.Video.Stream == null || (container.Video.Decoder.HasFinished == container.Video.Packets.Serial && container.Video.Frames.PendingCount == 0)))
                {
                    if (o.loop != 1 && (o.loop == 0 || (--o.loop) > 0))
                    {
                        stream_seek(container, o.start_time != ffmpeg.AV_NOPTS_VALUE ? o.start_time : 0, 0, 0);
                    }
                    else if (o.autoexit)
                    {
                        ret = ffmpeg.AVERROR_EOF;
                        goto fail;
                    }
                }

                var pkt = ffmpeg.av_packet_alloc();
                ret = ffmpeg.av_read_frame(ic, pkt);
                if (ret < 0)
                {
                    if ((ret == ffmpeg.AVERROR_EOF || ffmpeg.avio_feof(ic->pb) != 0) && !container.eof)
                    {
                        if (container.Video.StreamIndex >= 0)
                            container.Video.Packets.PutNull(container.Video.StreamIndex);
                        if (container.Audio.StreamIndex >= 0)
                            container.Audio.Packets.PutNull(container.Audio.StreamIndex);
                        if (container.Subtitle.StreamIndex >= 0)
                            container.Subtitle.Packets.PutNull(container.Subtitle.StreamIndex);
                        container.eof = true;
                    }
                    if (ic->pb != null && ic->pb->error != 0)
                    {
                        if (o.autoexit)
                            goto fail;
                        else
                            break;
                    }

                    container.continue_read_thread.WaitOne(10);

                    continue;
                }
                else
                {
                    container.eof = false;
                }

                /* check if packet is in play range specified by user, then queue, otherwise discard */
                stream_start_time = ic->streams[pkt->stream_index]->start_time;
                pkt_ts = pkt->pts == ffmpeg.AV_NOPTS_VALUE ? pkt->dts : pkt->pts;
                pkt_in_play_range = o.duration == ffmpeg.AV_NOPTS_VALUE ||
                        (pkt_ts - (stream_start_time != ffmpeg.AV_NOPTS_VALUE ? stream_start_time : 0)) *
                        ffmpeg.av_q2d(ic->streams[pkt->stream_index]->time_base) -
                        (double)(o.start_time != ffmpeg.AV_NOPTS_VALUE ? o.start_time : 0) / 1000000
                        <= ((double)o.duration / 1000000);
                if (pkt->stream_index == container.Audio.StreamIndex && pkt_in_play_range)
                {
                    container.Audio.Packets.Put(pkt);
                }
                else if (pkt->stream_index == container.Video.StreamIndex && pkt_in_play_range
                         && (container.Video.Stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                {
                    container.Video.Packets.Put(pkt);
                }
                else if (pkt->stream_index == container.Subtitle.StreamIndex && pkt_in_play_range)
                {
                    container.Subtitle.Packets.Put(pkt);
                }
                else
                {
                    ffmpeg.av_packet_unref(pkt);
                }
            }

            ret = 0;
        fail:
            if (ic != null && container.ic == null)
                ffmpeg.avformat_close_input(&ic);

            if (ret != 0)
            {
                SDL.SDL_Event evt = new();
                evt.type = (SDL.SDL_EventType)FF_QUIT_EVENT;
                // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                SDL.SDL_PushEvent(ref evt);
            }

            return; // 0;
        }

        static MediaContainer stream_open(ProgramOptions options)
        {
            var container = new MediaContainer(options);

            var o = container.Options;
            container.Video.LastStreamIndex = container.Video.StreamIndex = -1;
            container.Audio.LastStreamIndex = container.Audio.StreamIndex = -1;
            container.Subtitle.LastStreamIndex = container.Subtitle.StreamIndex = -1;
            container.filename = o.input_filename;
            if (string.IsNullOrWhiteSpace(container.filename))
                goto fail;

            container.iformat = o.file_iformat;
            container.ytop = 0;
            container.xleft = 0;

            /* start video display */
            container.Video.Frames = new(container.Video.Packets, Constants.VIDEO_PICTURE_QUEUE_SIZE, true);
            container.Subtitle.Frames = new(container.Subtitle.Packets, Constants.SUBPICTURE_QUEUE_SIZE, false);
            container.Audio.Frames = new(container.Audio.Packets, Constants.SAMPLE_QUEUE_SIZE, true);

            container.VideoClock = new Clock(container.Video.Packets);
            container.AudioClock = new Clock(container.Audio.Packets);
            container.ExternalClock = new Clock(container.ExternalClock);

            container.audio_clock_serial = -1;
            if (container.Options.startup_volume < 0)
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"-volume={container.Options.startup_volume} < 0, setting to 0\n");

            if (container.Options.startup_volume > 100)
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"-volume={container.Options.startup_volume} > 100, setting to 100\n");

            container.Options.startup_volume = Helpers.av_clip(container.Options.startup_volume, 0, 100);
            container.Options.startup_volume = Helpers.av_clip(SDL.SDL_MIX_MAXVOLUME * container.Options.startup_volume / 100, 0, SDL.SDL_MIX_MAXVOLUME);
            container.audio_volume = container.Options.startup_volume;
            container.muted = false;
            container.ClockSyncMode = container.Options.av_sync_type;
            container.read_tid = new Thread(read_thread) { IsBackground = true, Name = nameof(read_thread) };
            container.read_tid.Start(container);

            if (container.read_tid == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "SDL_CreateThread(): (new Thread())\n");
                goto fail;
            }

            return container;

        fail:
            stream_close(container);
            return null;
        }

        static void stream_cycle_channel(MediaContainer container, AVMediaType codecType)
        {
            AVFormatContext* ic = container.ic;
            int start_index, stream_index;
            int old_index;
            AVStream* st;
            AVProgram* p = null;
            int nb_streams = (int)container.ic->nb_streams;

            if (codecType == (int)AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                start_index = container.Video.LastStreamIndex;
                old_index = container.Video.StreamIndex;
            }
            else if (codecType == AVMediaType.AVMEDIA_TYPE_AUDIO)
            {
                start_index = container.Audio.LastStreamIndex;
                old_index = container.Audio.StreamIndex;
            }
            else
            {
                start_index = container.Subtitle.LastStreamIndex;
                old_index = container.Subtitle.StreamIndex;
            }
            stream_index = start_index;

            if (codecType != (int)AVMediaType.AVMEDIA_TYPE_VIDEO && container.Video.StreamIndex != -1)
            {
                p = ffmpeg.av_find_program_from_stream(ic, null, container.Video.StreamIndex);
                if (p != null)
                {
                    nb_streams = (int)p->nb_stream_indexes;
                    for (start_index = 0; start_index < nb_streams; start_index++)
                        if (p->stream_index[start_index] == stream_index)
                            break;
                    if (start_index == nb_streams)
                        start_index = -1;
                    stream_index = start_index;
                }
            }

            for (; ; )
            {
                if (++stream_index >= nb_streams)
                {
                    if (codecType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        stream_index = -1;
                        container.Subtitle.LastStreamIndex = -1;
                        goto the_end;
                    }
                    if (start_index == -1)
                        return;
                    stream_index = 0;
                }
                if (stream_index == start_index)
                    return;
                st = container.ic->streams[p != null ? p->stream_index[stream_index] : stream_index];
                if (st->codecpar->codec_type == codecType)
                {
                    /* check that parameters are OK */
                    switch ((AVMediaType)codecType)
                    {
                        case AVMediaType.AVMEDIA_TYPE_AUDIO:
                            if (st->codecpar->sample_rate != 0 &&
                                st->codecpar->channels != 0)
                                goto the_end;
                            break;
                        case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                            goto the_end;
                        default:
                            break;
                    }
                }
            }
        the_end:
            if (p != null && stream_index != -1)
                stream_index = (int)p->stream_index[stream_index];
            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"Switch {ffmpeg.av_get_media_type_string((AVMediaType)codecType)} stream from #{old_index} to #{stream_index}\n");

            stream_component_close(container, old_index);
            stream_component_open(container, stream_index);
        }

        static void toggle_audio_display(MediaContainer container)
        {
            int next = (int)container.show_mode;
            do
            {
                next = (next + 1) % (int)ShowMode.Last;
            } while (next != (int)container.show_mode && (next == (int)ShowMode.Video && container.Video.Stream == null || next != (int)ShowMode.Video && container.Audio.Stream == null));
            if ((int)container.show_mode != next)
            {
                container.force_refresh = true;
                container.show_mode = (ShowMode)next;
            }
        }

        static void refresh_loop_wait_event(MediaContainer container, out SDL.SDL_Event @event)
        {
            double remaining_time = 0.0;
            SDL.SDL_PumpEvents();
            var events = new SDL.SDL_Event[1];

            while (SDL.SDL_PeepEvents(events, 1, SDL.SDL_eventaction.SDL_GETEVENT, SDL.SDL_EventType.SDL_FIRSTEVENT, SDL.SDL_EventType.SDL_LASTEVENT) == 0)
            {
                if (!container.Options.cursor_hidden && ffmpeg.av_gettime_relative() - container.Options.cursor_last_shown > Constants.CURSOR_HIDE_DELAY)
                {
                    SDL.SDL_ShowCursor(0);
                    container.Options.cursor_hidden = true;
                }

                if (remaining_time > 0.0)
                    ffmpeg.av_usleep((uint)(remaining_time * 1000000.0));

                remaining_time = Constants.REFRESH_RATE;

                if (container.show_mode != ShowMode.None && (!container.paused || container.force_refresh))
                    SdlRenderer.video_refresh(container, ref remaining_time);

                SDL.SDL_PumpEvents();
            }

            @event = events[0];
        }

        static void seek_chapter(MediaContainer container, int incr)
        {
            var i = 0;
            var pos = (long)(container.MasterTime * ffmpeg.AV_TIME_BASE);

            if (container.ic->nb_chapters <= 0)
                return;

            /* find the current chapter */
            for (i = 0; i < container.ic->nb_chapters; i++)
            {
                AVChapter* ch = container.ic->chapters[i];
                if (ffmpeg.av_compare_ts(pos, Constants.AV_TIME_BASE_Q, ch->start, ch->time_base) < 0)
                {
                    i--;
                    break;
                }
            }

            i += incr;
            i = Math.Max(i, 0);
            if (i >= container.ic->nb_chapters)
                return;

            ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Seeking to chapter {i}.\n");
            stream_seek(container, ffmpeg.av_rescale_q(container.ic->chapters[i]->start, container.ic->chapters[i]->time_base, Constants.AV_TIME_BASE_Q), 0, 0);
        }

        /* handle an event sent by the GUI */
        static void event_loop(MediaContainer container)
        {
            SDL.SDL_Event @event;
            double incr, pos, frac;

            while (true)
            {
                double x;
                refresh_loop_wait_event(container, out @event);
                switch ((int)@event.type)
                {
                    case (int)SDL.SDL_EventType.SDL_KEYDOWN:
                        if (container.Options.exit_on_keydown || @event.key.keysym.sym == SDL.SDL_Keycode.SDLK_ESCAPE || @event.key.keysym.sym == SDL.SDL_Keycode.SDLK_q)
                        {
                            do_exit(container);
                            break;
                        }
                        // If we don't yet have a window, skip all key events, because read_thread might still be initializing...
                        if (container.width <= 0)
                            continue;
                        switch (@event.key.keysym.sym)
                        {
                            case SDL.SDL_Keycode.SDLK_f:
                                SdlRenderer.toggle_full_screen();
                                container.force_refresh = true;
                                break;
                            case SDL.SDL_Keycode.SDLK_p:
                            case SDL.SDL_Keycode.SDLK_SPACE:
                                toggle_pause(container);
                                break;
                            case SDL.SDL_Keycode.SDLK_m:
                                toggle_mute(container);
                                break;
                            case SDL.SDL_Keycode.SDLK_KP_MULTIPLY:
                            case SDL.SDL_Keycode.SDLK_0:
                                update_volume(container, 1, Constants.SDL_VOLUME_STEP);
                                break;
                            case SDL.SDL_Keycode.SDLK_KP_DIVIDE:
                            case SDL.SDL_Keycode.SDLK_9:
                                update_volume(container, -1, Constants.SDL_VOLUME_STEP);
                                break;
                            case SDL.SDL_Keycode.SDLK_s: // S: Step to next frame
                                step_to_next_frame(container);
                                break;
                            case SDL.SDL_Keycode.SDLK_a:
                                stream_cycle_channel(container, AVMediaType.AVMEDIA_TYPE_AUDIO);
                                break;
                            case SDL.SDL_Keycode.SDLK_v:
                                stream_cycle_channel(container, AVMediaType.AVMEDIA_TYPE_VIDEO);
                                break;
                            case SDL.SDL_Keycode.SDLK_c:
                                stream_cycle_channel(container, AVMediaType.AVMEDIA_TYPE_VIDEO);
                                stream_cycle_channel(container, AVMediaType.AVMEDIA_TYPE_AUDIO);
                                stream_cycle_channel(container, AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                                break;
                            case SDL.SDL_Keycode.SDLK_t:
                                stream_cycle_channel(container, AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                                break;
                            case SDL.SDL_Keycode.SDLK_w:

                                if (container.show_mode == ShowMode.Video && container.vfilter_idx < container.Options.nb_vfilters - 1)
                                {
                                    if (++container.vfilter_idx >= container.Options.nb_vfilters)
                                        container.vfilter_idx = 0;
                                }
                                else
                                {
                                    container.vfilter_idx = 0;
                                    toggle_audio_display(container);
                                }
                                break;
                            case SDL.SDL_Keycode.SDLK_PAGEUP:
                                if (container.ic->nb_chapters <= 1)
                                {
                                    incr = 600.0;
                                    goto do_seek;
                                }
                                seek_chapter(container, 1);
                                break;
                            case SDL.SDL_Keycode.SDLK_PAGEDOWN:
                                if (container.ic->nb_chapters <= 1)
                                {
                                    incr = -600.0;
                                    goto do_seek;
                                }
                                seek_chapter(container, -1);
                                break;
                            case SDL.SDL_Keycode.SDLK_LEFT:
                                incr = container.Options.seek_interval != 0 ? -container.Options.seek_interval : -10.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_RIGHT:
                                incr = container.Options.seek_interval != 0 ? container.Options.seek_interval : 10.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_UP:
                                incr = 60.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_DOWN:
                                incr = -60.0;
                            do_seek:
                                if (container.Options.seek_by_bytes != 0)
                                {
                                    pos = -1;
                                    if (pos < 0 && container.Video.StreamIndex >= 0)
                                        pos = container.Video.Frames.LastPosition;
                                    if (pos < 0 && container.Audio.StreamIndex >= 0)
                                        pos = container.Audio.Frames.LastPosition;
                                    if (pos < 0)
                                        pos = ffmpeg.avio_tell(container.ic->pb);
                                    if (container.ic->bit_rate != 0)
                                        incr *= container.ic->bit_rate / 8.0;
                                    else
                                        incr *= 180000.0;
                                    pos += incr;
                                    stream_seek(container, (long)pos, (long)incr, 1);
                                }
                                else
                                {
                                    pos = container.MasterTime;
                                    if (double.IsNaN(pos))
                                        pos = (double)container.seek_pos / ffmpeg.AV_TIME_BASE;
                                    pos += incr;
                                    if (container.ic->start_time != ffmpeg.AV_NOPTS_VALUE && pos < container.ic->start_time / (double)ffmpeg.AV_TIME_BASE)
                                        pos = container.ic->start_time / (double)ffmpeg.AV_TIME_BASE;
                                    stream_seek(container, (long)(pos * ffmpeg.AV_TIME_BASE), (long)(incr * ffmpeg.AV_TIME_BASE), 0);
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                        if (container.Options.exit_on_mousedown)
                        {
                            do_exit(container);
                            break;
                        }

                        if (@event.button.button == SDL.SDL_BUTTON_LEFT)
                        {
                            last_mouse_left_click = 0;
                            if (ffmpeg.av_gettime_relative() - last_mouse_left_click <= 500000)
                            {
                                SdlRenderer.toggle_full_screen();
                                container.force_refresh = true;
                                last_mouse_left_click = 0;
                            }
                            else
                            {
                                last_mouse_left_click = ffmpeg.av_gettime_relative();
                            }
                        }

                        break;
                    case (int)SDL.SDL_EventType.SDL_MOUSEMOTION:
                        if (container.Options.cursor_hidden)
                        {
                            SDL.SDL_ShowCursor(1);
                            container.Options.cursor_hidden = false;
                        }
                        container.Options.cursor_last_shown = ffmpeg.av_gettime_relative();
                        if (@event.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)
                        {
                            if (@event.button.button != SDL.SDL_BUTTON_RIGHT)
                                break;
                            x = @event.button.x;
                        }
                        else
                        {
                            if ((@event.motion.state & SDL.SDL_BUTTON_RMASK) == 0)
                                break;
                            x = @event.motion.x;
                        }
                        if (container.Options.seek_by_bytes != 0 || container.ic->duration <= 0)
                        {
                            long size = ffmpeg.avio_size(container.ic->pb);
                            stream_seek(container, (long)(size * x / container.width), 0, 1);
                        }
                        else
                        {
                            long ts;
                            int ns, hh, mm, ss;
                            int tns, thh, tmm, tss;
                            tns = (int)(container.ic->duration / 1000000L);
                            thh = tns / 3600;
                            tmm = (tns % 3600) / 60;
                            tss = (tns % 60);
                            frac = x / container.width;
                            ns = (int)(frac * tns);
                            hh = ns / 3600;
                            mm = (ns % 3600) / 60;
                            ss = (ns % 60);
                            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, "Seek to {(frac * 100)} ({hh}:{mm}:{ss}) of total duration ({thh}:{tmm}:{tss})       \n");
                            ts = (long)(frac * container.ic->duration);
                            if (container.ic->start_time != ffmpeg.AV_NOPTS_VALUE)
                                ts += container.ic->start_time;
                            stream_seek(container, ts, 0, 0);
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_WINDOWEVENT:
                        switch (@event.window.windowEvent)
                        {
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SIZE_CHANGED:
                                SdlRenderer.screen_width = container.width = @event.window.data1;
                                SdlRenderer.screen_height = container.height = @event.window.data2;
                                if (container.vis_texture != IntPtr.Zero)
                                {
                                    SDL.SDL_DestroyTexture(container.vis_texture);
                                    container.vis_texture = IntPtr.Zero;
                                }
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_EXPOSED:
                                container.force_refresh = true;
                                break;
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_QUIT:
                    case FF_QUIT_EVENT:
                        do_exit(container);
                        break;
                    default:
                        break;
                }
            }
        }

        public static void MainPort(string[] args)
        {
            var o = new ProgramOptions
            {
                input_filename = @"C:\Users\unosp\OneDrive\ffme-testsuite\video-subtitles-03.mkv",
                audio_disable = false,
                subtitle_disable = true,
                // av_sync_type = ClockSync.Video
            };

            Helpers.LoadNativeLibraries();
            ffmpeg.av_log_set_flags(ffmpeg.AV_LOG_SKIP_REPEATED);
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

            /* register all codecs, demux and protocols */
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();

            //init_opts();

            //signal(SIGINT, sigterm_handler); /* Interrupt (ANSI).    */
            //signal(SIGTERM, sigterm_handler); /* Termination (ANSI).  */

            if (string.IsNullOrWhiteSpace(o.input_filename))
                Environment.Exit(1);

            SdlRenderer = new();
            if (!SdlRenderer.Initialize(o))
                do_exit(null);

            GlobalVideoState = stream_open(o);

            if (GlobalVideoState == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Failed to initialize VideoState!\n");
                do_exit(null);
            }

            event_loop(GlobalVideoState);
        }


    }
}
