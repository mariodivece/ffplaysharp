namespace Unosquare.FFplaySharp.DirectPort
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Threading;
    using Unosquare.FFplaySharp.Primitives;

    public static unsafe class PortedProgram
    {
        // TODO: cmdutils.c
        // https://github.com/FFmpeg/FFmpeg/blob/master/fftools/cmdutils.c
        /* current context */
        

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

        static SDL.SDL_Event refresh_loop_wait_event(MediaContainer container)
        {
            var remainingTime = 0d;
            SDL.SDL_PumpEvents();
            var events = new SDL.SDL_Event[1];

            while (SDL.SDL_PeepEvents(events, 1, SDL.SDL_eventaction.SDL_GETEVENT, SDL.SDL_EventType.SDL_FIRSTEVENT, SDL.SDL_EventType.SDL_LASTEVENT) == 0)
            {
                if (!container.Options.cursor_hidden && Clock.SystemTime - container.Options.cursor_last_shown > Constants.CURSOR_HIDE_DELAY)
                {
                    SDL.SDL_ShowCursor(0);
                    container.Options.cursor_hidden = true;
                }

                if (remainingTime > 0.0)
                    Thread.Sleep(TimeSpan.FromSeconds(remainingTime));

                remainingTime = Constants.REFRESH_RATE;

                if (container.show_mode != ShowMode.None && (!container.IsPaused || SdlRenderer.force_refresh))
                    SdlRenderer.video_refresh(container, ref remainingTime);

                SDL.SDL_PumpEvents();
            }

            return events[0];
        }

        /* handle an event sent by the GUI */
        static void event_loop(MediaContainer container)
        {
            double incr, pos, x;

            while (true)
            {
                // double x;
                var sdlEvent = refresh_loop_wait_event(container);
                switch ((int)sdlEvent.type)
                {
                    case (int)SDL.SDL_EventType.SDL_KEYDOWN:
                        if (container.Options.exit_on_keydown || sdlEvent.key.keysym.sym == SDL.SDL_Keycode.SDLK_ESCAPE || sdlEvent.key.keysym.sym == SDL.SDL_Keycode.SDLK_q)
                        {
                            do_exit(container);
                            break;
                        }

                        // If we don't yet have a window, skip all key events, because read_thread might still be initializing...
                        if (container.width <= 0)
                            continue;
                        switch (sdlEvent.key.keysym.sym)
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
                                SdlRenderer.update_volume(1, Constants.SDL_VOLUME_STEP);
                                break;
                            case SDL.SDL_Keycode.SDLK_KP_DIVIDE:
                            case SDL.SDL_Keycode.SDLK_9:
                                SdlRenderer.update_volume(-1, Constants.SDL_VOLUME_STEP);
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
                                    pos = container.CurrentPosition;

                                    if (container.InputContext->bit_rate != 0)
                                        incr *= container.InputContext->bit_rate / 8.0;
                                    else
                                        incr *= 180000.0;

                                    pos += incr;
                                    container.SeekByPosition((long)pos, (long)incr);
                                }
                                else
                                {
                                    pos = container.MasterTime;
                                    if (pos.IsNaN())
                                        pos = container.SeekAbsoluteTarget / Clock.TimeBaseMicros;
                                    pos += incr;
                                    if (container.InputContext->start_time.IsValidPts() && pos < container.InputContext->start_time / Clock.TimeBaseMicros)
                                        pos = container.InputContext->start_time / Clock.TimeBaseMicros;
                                    container.SeekByTimestamp(Convert.ToInt64(pos * Clock.TimeBaseMicros), Convert.ToInt64(incr * Clock.TimeBaseMicros));
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

                        if (sdlEvent.button.button == SDL.SDL_BUTTON_LEFT)
                        {
                            // last_mouse_left_click = 0;
                            if (Clock.SystemTime - SdlRenderer.last_mouse_left_click <= 0.5d)
                            {
                                SdlRenderer.toggle_full_screen();
                                SdlRenderer.force_refresh = true;
                                SdlRenderer.last_mouse_left_click = 0d;
                            }
                            else
                            {
                                SdlRenderer.last_mouse_left_click = Clock.SystemTime;
                            }
                        }

                        break;
                    case (int)SDL.SDL_EventType.SDL_MOUSEMOTION:
                        if (container.Options.cursor_hidden)
                        {
                            SDL.SDL_ShowCursor(1);
                            container.Options.cursor_hidden = false;
                        }
                        container.Options.cursor_last_shown = Clock.SystemTime;
                        if (sdlEvent.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)
                        {
                            if (sdlEvent.button.button != SDL.SDL_BUTTON_RIGHT)
                                break;
                            x = sdlEvent.button.x;
                        }
                        else
                        {
                            if ((sdlEvent.motion.state & SDL.SDL_BUTTON_RMASK) == 0)
                                break;
                            x = sdlEvent.motion.x;
                        }
                        if (container.Options.seek_by_bytes != 0 || container.InputContext->duration <= 0)
                        {
                            var fileSize = ffmpeg.avio_size(container.InputContext->pb);
                            container.SeekByPosition(Convert.ToInt64(fileSize * x / container.width));
                        }
                        else
                        {
                            var seekPercent = (x / container.width);
                            var durationSecs = (double)container.InputContext->duration / Clock.TimeBaseMicros;
                            var totalDuration = TimeSpan.FromSeconds(durationSecs);
                            var targetTime = TimeSpan.FromSeconds(seekPercent * durationSecs);
                            var targetPosition = Convert.ToInt64(seekPercent * container.InputContext->duration);

                            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"Seek to {(seekPercent * 100):0.00} ({targetTime}) of total duration ({totalDuration})       \n");
                            
                            if (container.InputContext->start_time.IsValidPts())
                                targetPosition += container.InputContext->start_time;

                            container.SeekByTimestamp(targetPosition);
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_WINDOWEVENT:
                        switch (sdlEvent.window.windowEvent)
                        {
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SIZE_CHANGED:
                                SdlRenderer.screen_width = container.width = sdlEvent.window.data1;
                                SdlRenderer.screen_height = container.height = sdlEvent.window.data2;
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
                input_filename = @"C:\Users\unosp\OneDrive\ffme-testsuite\video-subtitles-03.mkv", // video-hevc-stress-01.mkv", // video-subtitles-03.mkv",
                audio_disable = false,
                subtitle_disable = false,
                av_sync_type = ClockSync.Audio,
                startup_volume = 6
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
