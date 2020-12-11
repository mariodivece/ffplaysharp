namespace Unosquare.FFplaySharp.DirectPort
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Diagnostics;
    using System.Threading;

    public static unsafe class PortedProgram
    {


        // TODO: cmdutils.c
        // https://github.com/FFmpeg/FFmpeg/blob/master/fftools/cmdutils.c
        /* current context */        
        static long last_mouse_left_click;

        static MediaContainer GlobalVideoState;
        static MediaRenderer SdlRenderer;

        static void stream_component_close(MediaContainer container, int stream_index)
        {
            AVFormatContext* ic = container.InputContext;
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

            fixed (AVFormatContext** ic = &container.InputContext)
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

        static MediaContainer stream_open(ProgramOptions options, MediaRenderer renderer)
        {
            var container = new MediaContainer(options, renderer);

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
            container.StartReadThread();
            return container;

        fail:
            stream_close(container);
            return null;
        }

        static void stream_cycle_channel(MediaContainer container, AVMediaType codecType)
        {
            AVFormatContext* ic = container.InputContext;
            int start_index, stream_index;
            int old_index;
            AVStream* st;
            AVProgram* p = null;
            int nb_streams = (int)container.InputContext->nb_streams;

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
                st = container.InputContext->streams[p != null ? p->stream_index[stream_index] : stream_index];
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
            container.stream_component_open(stream_index);
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
                                container.step_to_next_frame();
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
                                if (container.InputContext->nb_chapters <= 1)
                                {
                                    incr = 600.0;
                                    goto do_seek;
                                }
                                container.seek_chapter(1);
                                break;
                            case SDL.SDL_Keycode.SDLK_PAGEDOWN:
                                if (container.InputContext->nb_chapters <= 1)
                                {
                                    incr = -600.0;
                                    goto do_seek;
                                }
                                container.seek_chapter(-1);
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
                                        pos = ffmpeg.avio_tell(container.InputContext->pb);
                                    if (container.InputContext->bit_rate != 0)
                                        incr *= container.InputContext->bit_rate / 8.0;
                                    else
                                        incr *= 180000.0;
                                    pos += incr;
                                    container.stream_seek((long)pos, (long)incr, 1);
                                }
                                else
                                {
                                    pos = container.MasterTime;
                                    if (double.IsNaN(pos))
                                        pos = (double)container.seek_pos / ffmpeg.AV_TIME_BASE;
                                    pos += incr;
                                    if (container.InputContext->start_time != ffmpeg.AV_NOPTS_VALUE && pos < container.InputContext->start_time / (double)ffmpeg.AV_TIME_BASE)
                                        pos = container.InputContext->start_time / (double)ffmpeg.AV_TIME_BASE;
                                    container.stream_seek((long)(pos * ffmpeg.AV_TIME_BASE), (long)(incr * ffmpeg.AV_TIME_BASE), 0);
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
                        if (container.Options.seek_by_bytes != 0 || container.InputContext->duration <= 0)
                        {
                            long size = ffmpeg.avio_size(container.InputContext->pb);
                            container.stream_seek((long)(size * x / container.width), 0, 1);
                        }
                        else
                        {
                            long ts;
                            int ns, hh, mm, ss;
                            int tns, thh, tmm, tss;
                            tns = (int)(container.InputContext->duration / 1000000L);
                            thh = tns / 3600;
                            tmm = (tns % 3600) / 60;
                            tss = (tns % 60);
                            frac = x / container.width;
                            ns = (int)(frac * tns);
                            hh = ns / 3600;
                            mm = (ns % 3600) / 60;
                            ss = (ns % 60);
                            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, "Seek to {(frac * 100)} ({hh}:{mm}:{ss}) of total duration ({thh}:{tmm}:{tss})       \n");
                            ts = (long)(frac * container.InputContext->duration);
                            if (container.InputContext->start_time != ffmpeg.AV_NOPTS_VALUE)
                                ts += container.InputContext->start_time;
                            container.stream_seek(ts, 0, 0);
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
                    case Constants.FF_QUIT_EVENT:
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

            GlobalVideoState = stream_open(o, SdlRenderer);
            SdlRenderer.Link(GlobalVideoState);

            if (GlobalVideoState == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Failed to initialize VideoState!\n");
                do_exit(null);
            }

            event_loop(GlobalVideoState);
        }


    }
}
