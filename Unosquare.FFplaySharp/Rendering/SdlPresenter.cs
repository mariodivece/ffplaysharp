namespace Unosquare.FFplaySharp.Rendering
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class SdlPresenter : IPresenter
    {
        private double LastMouseLeftClick;

        public IVideoRenderer Video { get; private set; }

        public IAudioRenderer Audio { get; private set; }

        public MediaContainer Container { get; private set; }

        public uint SdlInitFlags { get; set; }

        private ProgramOptions Options => Container.Options;

        public bool Initialize(MediaContainer container)
        {
            Container = container;
            Audio = new SdlAudioRenderer();
            Video = new SdlVideoRenderer();

            var o = Container.Options;
            if (o.display_disable)
                o.video_disable = true;

            SdlInitFlags = SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_TIMER;

            Audio.Initialize(this);

            if (o.display_disable)
                SdlInitFlags &= ~SDL.SDL_INIT_VIDEO;

            if (SDL.SDL_Init(SdlInitFlags) != 0)
            {
                Helpers.LogFatal($"Could not initialize SDL - {SDL.SDL_GetError()}\n");
                return false;
            }

            SDL.SDL_EventState(SDL.SDL_EventType.SDL_SYSWMEVENT, SDL.SDL_IGNORE);
            SDL.SDL_EventState(SDL.SDL_EventType.SDL_USEREVENT, SDL.SDL_IGNORE);

            Video.Initialize(this);
            return true;
        }

        public void Start()
        {
            event_loop();
        }

        public void Stop()
        {
            do_exit();
        }


        private SDL.SDL_Event refresh_loop_wait_event()
        {
            var remainingTime = 0d;
            SDL.SDL_PumpEvents();
            var events = new SDL.SDL_Event[1];

            while (SDL.SDL_PeepEvents(events, 1, SDL.SDL_eventaction.SDL_GETEVENT, SDL.SDL_EventType.SDL_FIRSTEVENT, SDL.SDL_EventType.SDL_LASTEVENT) == 0)
            {
                if (!Options.cursor_hidden && Clock.SystemTime - Options.cursor_last_shown > Constants.CURSOR_HIDE_DELAY)
                {
                    _ = SDL.SDL_ShowCursor(0);
                    Options.cursor_hidden = true;
                }

                if (remainingTime > 0.0)
                    ffmpeg.av_usleep(Convert.ToUInt32(remainingTime * ffmpeg.AV_TIME_BASE));

                remainingTime = Constants.REFRESH_RATE;

                if (Container.ShowMode != ShowMode.None && (!Container.IsPaused || Video.ForceRefresh))
                    Video.Present(ref remainingTime);

                SDL.SDL_PumpEvents();
            }

            return events[0];
        }


        private void do_exit()
        {
            if (Container != null)
                Container.Close();

            Video.Close();

            Container.Options.uninit_opts();
            Container.Options.vfilters_list.Clear();

            ffmpeg.avformat_network_deinit();
            if (Container.Options.show_status != 0)
                Console.WriteLine();

            SDL.SDL_Quit();
            Helpers.LogQuiet(string.Empty);
            Environment.Exit(0);
        }

        private void toggle_audio_display()
        {
            int next = (int)Container.ShowMode;
            do
            {
                next = (next + 1) % (int)ShowMode.Last;
            } while (next != (int)Container.ShowMode && (next == (int)ShowMode.Video && Container.Video.Stream == null || next != (int)ShowMode.Video && Container.Audio.Stream == null));
            if ((int)Container.ShowMode != next)
            {
                Video.ForceRefresh = true;
                Container.ShowMode = (ShowMode)next;
            }
        }


        /* handle an event sent by the GUI */
        private void event_loop()
        {
            double incr, pos, x;

            while (true)
            {
                // double x;
                var sdlEvent = refresh_loop_wait_event();
                switch ((int)sdlEvent.type)
                {
                    case (int)SDL.SDL_EventType.SDL_KEYDOWN:
                        if (Options.exit_on_keydown || sdlEvent.key.keysym.sym == SDL.SDL_Keycode.SDLK_ESCAPE || sdlEvent.key.keysym.sym == SDL.SDL_Keycode.SDLK_q)
                        {
                            do_exit();
                            break;
                        }

                        // If we don't yet have a window, skip all key events, because read_thread might still be initializing...
                        if (Container.width <= 0)
                            continue;
                        switch (sdlEvent.key.keysym.sym)
                        {
                            case SDL.SDL_Keycode.SDLK_f:
                                Video.ToggleFullScreen();
                                Video.ForceRefresh = true;
                                break;
                            case SDL.SDL_Keycode.SDLK_p:
                            case SDL.SDL_Keycode.SDLK_SPACE:
                                Container.TogglePause();
                                break;
                            case SDL.SDL_Keycode.SDLK_m:
                                Container.ToggleMute();
                                break;
                            case SDL.SDL_Keycode.SDLK_KP_MULTIPLY:
                            case SDL.SDL_Keycode.SDLK_0:
                                Audio.UpdateVolume(1, Constants.SDL_VOLUME_STEP);
                                break;
                            case SDL.SDL_Keycode.SDLK_KP_DIVIDE:
                            case SDL.SDL_Keycode.SDLK_9:
                                Audio.UpdateVolume(-1, Constants.SDL_VOLUME_STEP);
                                break;
                            case SDL.SDL_Keycode.SDLK_s: // S: Step to next frame
                                Container.StepToNextFrame();
                                break;
                            case SDL.SDL_Keycode.SDLK_a:
                                Container.StreamCycleChannel(AVMediaType.AVMEDIA_TYPE_AUDIO);
                                break;
                            case SDL.SDL_Keycode.SDLK_v:
                                Container.StreamCycleChannel(AVMediaType.AVMEDIA_TYPE_VIDEO);
                                break;
                            case SDL.SDL_Keycode.SDLK_c:
                                Container.StreamCycleChannel(AVMediaType.AVMEDIA_TYPE_VIDEO);
                                Container.StreamCycleChannel(AVMediaType.AVMEDIA_TYPE_AUDIO);
                                Container.StreamCycleChannel(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                                break;
                            case SDL.SDL_Keycode.SDLK_t:
                                Container.StreamCycleChannel(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                                break;
                            case SDL.SDL_Keycode.SDLK_w:

                                if (Container.ShowMode == ShowMode.Video && Container.Video.CurrentFilterIndex < Container.Options.nb_vfilters - 1)
                                {
                                    if (++Container.Video.CurrentFilterIndex >= Container.Options.nb_vfilters)
                                        Container.Video.CurrentFilterIndex = 0;
                                }
                                else
                                {
                                    Container.Video.CurrentFilterIndex = 0;
                                    toggle_audio_display();
                                }
                                break;
                            case SDL.SDL_Keycode.SDLK_PAGEUP:
                                if (Container.InputContext->nb_chapters <= 1)
                                {
                                    incr = 600.0;
                                    goto do_seek;
                                }
                                Container.ChapterSeek(1);
                                break;
                            case SDL.SDL_Keycode.SDLK_PAGEDOWN:
                                if (Container.InputContext->nb_chapters <= 1)
                                {
                                    incr = -600.0;
                                    goto do_seek;
                                }
                                Container.ChapterSeek(-1);
                                break;
                            case SDL.SDL_Keycode.SDLK_LEFT:
                                incr = Container.Options.seek_interval != 0 ? -Container.Options.seek_interval : -10.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_RIGHT:
                                incr = Container.Options.seek_interval != 0 ? Container.Options.seek_interval : 10.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_UP:
                                incr = 60.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_DOWN:
                                incr = -60.0;
                            do_seek:
                                if (Container.Options.seek_by_bytes != 0)
                                {
                                    pos = Container.StreamBytePosition;

                                    if (Container.InputContext->bit_rate != 0)
                                        incr *= Container.InputContext->bit_rate / 8.0;
                                    else
                                        incr *= 180000.0;

                                    pos += incr;
                                    Container.SeekByPosition((long)pos, (long)incr);
                                }
                                else
                                {
                                    pos = Container.MasterTime;
                                    if (pos.IsNaN())
                                        pos = Container.SeekAbsoluteTarget / Clock.TimeBaseMicros;
                                    pos += incr;
                                    if (Container.InputContext->start_time.IsValidPts() && pos < Container.InputContext->start_time / Clock.TimeBaseMicros)
                                        pos = Container.InputContext->start_time / Clock.TimeBaseMicros;
                                    Container.SeekByTimestamp(Convert.ToInt64(pos * Clock.TimeBaseMicros), Convert.ToInt64(incr * Clock.TimeBaseMicros));
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                        if (Container.Options.exit_on_mousedown)
                        {
                            do_exit();
                            break;
                        }

                        if (sdlEvent.button.button == SDL.SDL_BUTTON_LEFT)
                        {
                            // last_mouse_left_click = 0;
                            if (Clock.SystemTime - LastMouseLeftClick <= 0.5d)
                            {
                                Video.ToggleFullScreen();
                                Video.ForceRefresh = true;
                                LastMouseLeftClick = 0d;
                            }
                            else
                            {
                                LastMouseLeftClick = Clock.SystemTime;
                            }
                        }

                        break;
                    case (int)SDL.SDL_EventType.SDL_MOUSEMOTION:
                        if (Container.Options.cursor_hidden)
                        {
                            SDL.SDL_ShowCursor(1);
                            Container.Options.cursor_hidden = false;
                        }
                        Container.Options.cursor_last_shown = Clock.SystemTime;
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
                        if (Container.Options.seek_by_bytes != 0 || Container.InputContext->duration <= 0)
                        {
                            var fileSize = ffmpeg.avio_size(Container.InputContext->pb);
                            Container.SeekByPosition(Convert.ToInt64(fileSize * x / Container.width));
                        }
                        else
                        {
                            var seekPercent = (x / Container.width);
                            var durationSecs = Container.InputContext->duration / Clock.TimeBaseMicros;
                            var totalDuration = TimeSpan.FromSeconds(durationSecs);
                            var targetTime = TimeSpan.FromSeconds(seekPercent * durationSecs);
                            var targetPosition = Convert.ToInt64(seekPercent * Container.InputContext->duration);

                            Helpers.LogInfo($"Seek to {(seekPercent * 100):0.00} ({targetTime}) of total duration ({totalDuration})       \n");

                            if (Container.InputContext->start_time.IsValidPts())
                                targetPosition += Container.InputContext->start_time;

                            Container.SeekByTimestamp(targetPosition);
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_WINDOWEVENT:
                        switch (sdlEvent.window.windowEvent)
                        {
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SIZE_CHANGED:
                                Video.screen_width = Container.width = sdlEvent.window.data1;
                                Video.screen_height = Container.height = sdlEvent.window.data2;
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_EXPOSED:
                                Video.ForceRefresh = true;
                                break;
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_QUIT:
                    case Constants.FF_QUIT_EVENT:
                        do_exit();
                        break;
                    default:
                        break;
                }
            }
        }

    }
}
