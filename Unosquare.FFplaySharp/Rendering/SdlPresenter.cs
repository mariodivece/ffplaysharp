namespace Unosquare.FFplaySharp.Rendering;

using SDL2;


public unsafe class SdlPresenter : IPresenter
{
    private double LastMouseLeftClick;
    private double LastCursorShownTime;
    private bool IsCursorHidden = false;

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
        if (o.IsDisplayDisabled)
            o.IsVideoDisabled = true;

        SdlInitFlags = SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_TIMER;

        Audio.Initialize(this);

        if (o.IsDisplayDisabled)
            SdlInitFlags &= ~SDL.SDL_INIT_VIDEO;

        if (SDL.SDL_Init(SdlInitFlags) != 0)
        {
            ($"Could not initialize SDL - {SDL.SDL_GetError()}").LogFatal();
            return false;
        }

        SDL.SDL_EventState(SDL.SDL_EventType.SDL_SYSWMEVENT, SDL.SDL_IGNORE);
        SDL.SDL_EventState(SDL.SDL_EventType.SDL_USEREVENT, SDL.SDL_IGNORE);

        Video.Initialize(this);
        return true;
    }

    /// <summary>
    /// Port of event_loop
    /// </summary>
    public void Start()
    {
        // handle an event sent by the GUI.
        double incr, pos;

        while (true)
        {
            var mouseX = 0d;
            var sdlEvent = RetrieveNextEvent();
            switch ((int)sdlEvent.type)
            {
                case (int)SDL.SDL_EventType.SDL_KEYDOWN:
                    if (Options.ExitOnKeyDown || sdlEvent.key.keysym.sym == SDL.SDL_Keycode.SDLK_ESCAPE || sdlEvent.key.keysym.sym == SDL.SDL_Keycode.SDLK_q)
                    {
                        Stop();
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

                            if (Container.ShowMode == ShowMode.Video && Container.Video.CurrentFilterIndex < Container.Options.VideoFilterGraphs.Count - 1)
                            {
                                if (++Container.Video.CurrentFilterIndex >= Container.Options.VideoFilterGraphs.Count)
                                    Container.Video.CurrentFilterIndex = 0;
                            }
                            else
                            {
                                Container.Video.CurrentFilterIndex = 0;
                                ToggleAudioDisplay();
                            }
                            break;
                        case SDL.SDL_Keycode.SDLK_PAGEUP:
                            if (Container.Input.Chapters.Count <= 1)
                            {
                                incr = 600.0;
                                goto do_seek;
                            }
                            Container.ChapterSeek(1);
                            break;
                        case SDL.SDL_Keycode.SDLK_PAGEDOWN:
                            if (Container.Input.Chapters.Count <= 1)
                            {
                                incr = -600.0;
                                goto do_seek;
                            }
                            Container.ChapterSeek(-1);
                            break;
                        case SDL.SDL_Keycode.SDLK_LEFT:
                            incr = Container.Options.SeekInterval != 0 ? -Container.Options.SeekInterval : -10.0;
                            goto do_seek;
                        case SDL.SDL_Keycode.SDLK_RIGHT:
                            incr = Container.Options.SeekInterval != 0 ? Container.Options.SeekInterval : 10.0;
                            goto do_seek;
                        case SDL.SDL_Keycode.SDLK_UP:
                            incr = 60.0;
                            goto do_seek;
                        case SDL.SDL_Keycode.SDLK_DOWN:
                            incr = -60.0;
                        do_seek:
                            if (Container.Options.IsByteSeekingEnabled != 0)
                            {
                                pos = Container.StreamBytePosition;

                                if (Container.Input.Pointer->bit_rate != 0)
                                    incr *= Container.Input.Pointer->bit_rate / 8.0;
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
                                if (Container.Input.StartTime.IsValidPts() && pos < Container.Input.StartTime / Clock.TimeBaseMicros)
                                    pos = Container.Input.StartTime / Clock.TimeBaseMicros;
                                Container.SeekByTimestamp(Convert.ToInt64(pos * Clock.TimeBaseMicros), Convert.ToInt64(incr * Clock.TimeBaseMicros));
                            }
                            break;
                        default:
                            break;
                    }
                    break;
                case (int)SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                    if (Container.Options.ExitOnMouseDown)
                    {
                        Stop();
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
                    if (IsCursorHidden)
                    {
                        SDL.SDL_ShowCursor(1);
                        IsCursorHidden = false;
                    }

                    LastCursorShownTime = Clock.SystemTime;
                    if (sdlEvent.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)
                    {
                        if (sdlEvent.button.button != SDL.SDL_BUTTON_RIGHT)
                            break;
                        mouseX = sdlEvent.button.x;
                    }
                    else
                    {
                        if ((sdlEvent.motion.state & SDL.SDL_BUTTON_RMASK) == 0)
                            break;
                        mouseX = sdlEvent.motion.x;
                    }
                    if (Container.Options.IsByteSeekingEnabled != 0 || Container.Input.Duration <= 0)
                    {
                        var fileSize = Container.Input.IO.Size;
                        Container.SeekByPosition(Convert.ToInt64(fileSize * mouseX / Container.width));
                    }
                    else
                    {
                        var seekPercent = (mouseX / Container.width);
                        var durationSecs = Container.Input.DurationSeconds;
                        var totalDuration = TimeSpan.FromSeconds(durationSecs);
                        var targetTime = TimeSpan.FromSeconds(seekPercent * durationSecs);
                        var targetPosition = Convert.ToInt64(seekPercent * Container.Input.Duration);

                        ($"Seek to {(seekPercent * 100):0.00} ({targetTime}) of total duration ({totalDuration})").LogInfo();

                        if (Container.Input.StartTime.IsValidPts())
                            targetPosition += Container.Input.StartTime;

                        Container.SeekByTimestamp(targetPosition);
                    }
                    break;
                case (int)SDL.SDL_EventType.SDL_WINDOWEVENT:
                    switch (sdlEvent.window.windowEvent)
                    {
                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SIZE_CHANGED:
                            Video.ScreenWidth = Container.width = sdlEvent.window.data1;
                            Video.ScreenHeight = Container.height = sdlEvent.window.data2;
                            break;
                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_EXPOSED:
                            Video.ForceRefresh = true;
                            break;
                    }
                    break;
                case (int)SDL.SDL_EventType.SDL_QUIT:
                case Constants.FF_QUIT_EVENT:
                    Stop();
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Port of do_exit
    /// </summary>
    public void Stop()
    {
        if (Container != null)
            Container.Close();

        Video.Close();
        Container.Options.VideoFilterGraphs.Clear();

        ffmpeg.avformat_network_deinit();
        if (Container.Options.ShowStatus != ThreeState.Off)
            Console.WriteLine();

        SDL.SDL_Quit();
        (string.Empty).LogQuiet();
        ReferenceCounter.VeirfyZero();
        Environment.Exit(0);
    }

    /// <summary>
    /// Port of refresh_loop_wait_event.
    /// </summary>
    /// <returns></returns>
    private SDL.SDL_Event RetrieveNextEvent()
    {
        var remainingTime = 0d;
        SDL.SDL_PumpEvents();
        var events = new SDL.SDL_Event[1];

        while (SDL.SDL_PeepEvents(events, 1, SDL.SDL_eventaction.SDL_GETEVENT, SDL.SDL_EventType.SDL_FIRSTEVENT, SDL.SDL_EventType.SDL_LASTEVENT) == 0)
        {
            if (!IsCursorHidden && Clock.SystemTime - LastCursorShownTime > Constants.CURSOR_HIDE_DELAY)
            {
                _ = SDL.SDL_ShowCursor(0);
                IsCursorHidden = true;
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

    /// <summary>
    /// Port of toggle_audio_display.
    /// </summary>
    private void ToggleAudioDisplay()
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


}
