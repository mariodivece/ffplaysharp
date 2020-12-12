namespace Unosquare.FFplaySharp.DirectPort
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;

    public static unsafe class PortedProgram
    {
        // TODO: cmdutils.c
        // https://github.com/FFmpeg/FFmpeg/blob/master/fftools/cmdutils.c
        /* current context */
        static long last_mouse_left_click;

        static MediaContainer GlobalVideoState;
        static MediaRenderer SdlRenderer;

        static void do_exit(MediaContainer container)
        {
            if (container != null)
            {
                container.stream_close();
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

        static void update_volume(MediaContainer container, int sign, double step)
        {
            var volume_level = container.audio_volume > 0 ? (20 * Math.Log(container.audio_volume / (double)SDL.SDL_MIX_MAXVOLUME) / Math.Log(10)) : -1000.0;
            var new_volume = (int)Math.Round(SDL.SDL_MIX_MAXVOLUME * Math.Pow(10.0, (volume_level + sign * step) / 20.0), 0);
            container.audio_volume = Helpers.av_clip(container.audio_volume == new_volume ? (container.audio_volume + sign) : new_volume, 0, SDL.SDL_MIX_MAXVOLUME);
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
                SdlRenderer.force_refresh = true;
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

                if (container.show_mode != ShowMode.None && (!container.IsPaused || SdlRenderer.force_refresh))
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
                                SdlRenderer.force_refresh = true;
                                break;
                            case SDL.SDL_Keycode.SDLK_p:
                            case SDL.SDL_Keycode.SDLK_SPACE:
                                container.toggle_pause();
                                break;
                            case SDL.SDL_Keycode.SDLK_m:
                                container.toggle_mute();
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
                                container.stream_cycle_channel(AVMediaType.AVMEDIA_TYPE_AUDIO);
                                break;
                            case SDL.SDL_Keycode.SDLK_v:
                                container.stream_cycle_channel(AVMediaType.AVMEDIA_TYPE_VIDEO);
                                break;
                            case SDL.SDL_Keycode.SDLK_c:
                                container.stream_cycle_channel(AVMediaType.AVMEDIA_TYPE_VIDEO);
                                container.stream_cycle_channel(AVMediaType.AVMEDIA_TYPE_AUDIO);
                                container.stream_cycle_channel(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                                break;
                            case SDL.SDL_Keycode.SDLK_t:
                                container.stream_cycle_channel(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
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
                            // last_mouse_left_click = 0;
                            if (ffmpeg.av_gettime_relative() - last_mouse_left_click <= 500000)
                            {
                                SdlRenderer.toggle_full_screen();
                                SdlRenderer.force_refresh = true;
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
                            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"Seek to {(frac * 100)} ({hh}:{mm}:{ss}) of total duration ({thh}:{tmm}:{tss})       \n");
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
                                SdlRenderer.force_refresh = true;
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
                subtitle_disable = false,
                av_sync_type = ClockSync.Audio
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

            GlobalVideoState = MediaContainer.stream_open(o, SdlRenderer);
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
