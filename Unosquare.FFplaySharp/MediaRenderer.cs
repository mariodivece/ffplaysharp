namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class MediaRenderer
    {
        private readonly SDL.SDL_AudioCallback AudioCallback;
        private uint AudioDeviceId;

        public double last_mouse_left_click;

        public double audio_callback_time { get; private set; }

        private int audio_buf_index; /* in bytes */
        private int audio_write_buf_size;

        public int default_width = 640;
        public int default_height = 480;

        private IntPtr RenderingWindow;
        private IntPtr SdlRenderer;
        public SDL.SDL_RendererInfo SdlRendererInfo;

        public string window_title;

        public IntPtr sub_texture;
        public IntPtr vid_texture;

        private int screen_left = SDL.SDL_WINDOWPOS_CENTERED;
        private int screen_top = SDL.SDL_WINDOWPOS_CENTERED;
        private bool is_full_screen;
        private double rdftspeed = 0.02;

        public int screen_width = 0;
        public int screen_height = 0;

        public bool force_refresh;

        // inlined static variables
        public double last_time_status = 0;

        public int audio_volume;

        public MediaRenderer()
        {
            AudioCallback = new(sdl_audio_callback);
        }

        public MediaContainer Container { get; private set; }

        public static readonly Dictionary<AVPixelFormat, uint> sdl_texture_map = new()
        {
            { AVPixelFormat.AV_PIX_FMT_RGB8, SDL.SDL_PIXELFORMAT_RGB332 },
            { AVPixelFormat.AV_PIX_FMT_RGB444LE, SDL.SDL_PIXELFORMAT_RGB444 },
            { AVPixelFormat.AV_PIX_FMT_RGB555LE, SDL.SDL_PIXELFORMAT_RGB555 },
            { AVPixelFormat.AV_PIX_FMT_BGR555LE, SDL.SDL_PIXELFORMAT_BGR555 },
            { AVPixelFormat.AV_PIX_FMT_RGB565LE, SDL.SDL_PIXELFORMAT_RGB565 },
            { AVPixelFormat.AV_PIX_FMT_BGR565LE, SDL.SDL_PIXELFORMAT_BGR565 },
            { AVPixelFormat.AV_PIX_FMT_RGB24, SDL.SDL_PIXELFORMAT_RGB24 },
            { AVPixelFormat.AV_PIX_FMT_BGR24, SDL.SDL_PIXELFORMAT_BGR24 },
            { AVPixelFormat.AV_PIX_FMT_0RGB, SDL.SDL_PIXELFORMAT_RGB888 },
            { AVPixelFormat.AV_PIX_FMT_0BGR, SDL.SDL_PIXELFORMAT_BGR888 },
            { AVPixelFormat.AV_PIX_FMT_RGB0, SDL.SDL_PIXELFORMAT_RGBX8888 },
            { AVPixelFormat.AV_PIX_FMT_BGR0, SDL.SDL_PIXELFORMAT_BGRX8888 },
            { AVPixelFormat.AV_PIX_FMT_ARGB, SDL.SDL_PIXELFORMAT_ARGB8888 },
            { AVPixelFormat.AV_PIX_FMT_RGBA, SDL.SDL_PIXELFORMAT_RGBA8888 },
            { AVPixelFormat.AV_PIX_FMT_ABGR, SDL.SDL_PIXELFORMAT_ABGR8888 },
            { AVPixelFormat.AV_PIX_FMT_BGRA, SDL.SDL_PIXELFORMAT_BGRA8888 },
            { AVPixelFormat.AV_PIX_FMT_YUV420P, SDL.SDL_PIXELFORMAT_IYUV },
            { AVPixelFormat.AV_PIX_FMT_YUYV422, SDL.SDL_PIXELFORMAT_YUY2 },
            { AVPixelFormat.AV_PIX_FMT_UYVY422, SDL.SDL_PIXELFORMAT_UYVY },
            { AVPixelFormat.AV_PIX_FMT_NONE, SDL.SDL_PIXELFORMAT_UNKNOWN },
        };

        public int realloc_texture(ref IntPtr texture, uint new_format, int new_width, int new_height, SDL.SDL_BlendMode blendmode, bool init_texture)
        {
            if (texture.IsNull() || SDL.SDL_QueryTexture(texture, out var format, out var _, out var w, out var h) < 0 || new_width != w || new_height != h || new_format != format)
            {
                if (!texture.IsNull())
                    SDL.SDL_DestroyTexture(texture);

                texture = SDL.SDL_CreateTexture(SdlRenderer, new_format, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, new_width, new_height);
                if (texture.IsNull())
                    return -1;

                if (SDL.SDL_SetTextureBlendMode(texture, blendmode) < 0)
                    return -1;

                if (init_texture)
                {
                    SDL.SDL_Rect rect = new() { w = new_width, h = new_height, x = 0, y = 0 };
                    rect.w = new_width;
                    rect.h = new_height;
                    if (SDL.SDL_LockTexture(texture, ref rect, out var pixels, out var pitch) < 0)
                        return -1;

                    var ptr = (byte*)pixels;
                    for (var i = 0; i < pitch * new_height; i++)
                    {
                        ptr[i] = byte.MinValue;
                    }

                    SDL.SDL_UnlockTexture(texture);
                }

                ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Created {new_width}x{new_height} texture with {SDL.SDL_GetPixelFormatName(new_format)}.\n");
            }
            return 0;
        }

        public void video_image_display(MediaContainer container)
        {
            FrameHolder vp;
            FrameHolder sp = null;
            SDL.SDL_Rect rect = new();

            vp = container.Video.Frames.PeekLast();
            if (container.Subtitle.Stream != null)
            {
                if (container.Subtitle.Frames.PendingCount > 0)
                {
                    sp = container.Subtitle.Frames.Peek();

                    if (vp.Time >= sp.Time + ((float)sp.SubtitlePtr->start_display_time / 1000))
                    {
                        if (!sp.uploaded)
                        {
                            if (sp.Width <= 0 || sp.Height <= 0)
                            {
                                sp.Width = vp.Width;
                                sp.Height = vp.Height;
                            }

                            if (realloc_texture(ref sub_texture, SDL.SDL_PIXELFORMAT_ARGB8888, sp.Width, sp.Height, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND, true) < 0)
                                return;

                            for (var i = 0; i < sp.SubtitlePtr->num_rects; i++)
                            {
                                var sub_rect = new SDL.SDL_Rect
                                {
                                    x = sp.SubtitlePtr->rects[i]->x,
                                    y = sp.SubtitlePtr->rects[i]->y,
                                    w = sp.SubtitlePtr->rects[i]->w,
                                    h = sp.SubtitlePtr->rects[i]->h
                                };

                                sub_rect.x = Helpers.av_clip(sub_rect.x, 0, sp.Width);
                                sub_rect.y = Helpers.av_clip(sub_rect.y, 0, sp.Height);
                                sub_rect.w = Helpers.av_clip(sub_rect.w, 0, sp.Width - sub_rect.x);
                                sub_rect.h = Helpers.av_clip(sub_rect.h, 0, sp.Height - sub_rect.y);

                                container.Subtitle.ConvertContext = ffmpeg.sws_getCachedContext(container.Subtitle.ConvertContext,
                                    sub_rect.w, sub_rect.h, AVPixelFormat.AV_PIX_FMT_PAL8,
                                    sub_rect.w, sub_rect.h, AVPixelFormat.AV_PIX_FMT_BGRA,
                                    0, null, null, null);
                                if (container.Subtitle.ConvertContext == null)
                                {
                                    ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Cannot initialize the conversion context\n");
                                    return;
                                }
                                if (SDL.SDL_LockTexture(sub_texture, ref sub_rect, out var pixels, out var pitch) == 0)
                                {
                                    var targetStride = new[] { pitch };
                                    var targetScan = default(byte_ptrArray8);
                                    targetScan[0] = (byte*)pixels;

                                    ffmpeg.sws_scale(container.Subtitle.ConvertContext, sp.SubtitlePtr->rects[i]->data, sp.SubtitlePtr->rects[i]->linesize,
                                      0, sp.SubtitlePtr->rects[i]->h, targetScan, targetStride);

                                    SDL.SDL_UnlockTexture(sub_texture);
                                }
                            }
                            sp.uploaded = true;
                        }
                    }
                    else
                    {
                        sp = null;
                    }
                }
            }

            rect = calculate_display_rect(container.xleft, container.ytop, container.width, container.height, vp.Width, vp.Height, vp.Sar);

            if (!vp.uploaded)
            {
                if (upload_texture(ref vid_texture, vp.FramePtr, ref container.Video.ConvertContext) < 0)
                    return;
                vp.uploaded = true;
                vp.FlipVertical = vp.FramePtr->linesize[0] < 0;
            }

            var point = new SDL.SDL_Point();

            set_sdl_yuv_conversion_mode(vp.FramePtr);
            SDL.SDL_RenderCopyEx(SdlRenderer, vid_texture, ref rect, ref rect, 0, ref point, vp.FlipVertical ? SDL.SDL_RendererFlip.SDL_FLIP_VERTICAL : SDL.SDL_RendererFlip.SDL_FLIP_NONE);
            set_sdl_yuv_conversion_mode(null);

            if (sp != null)
            {
                int i;
                double xratio = (double)rect.w / (double)sp.Width;
                double yratio = (double)rect.h / (double)sp.Height;
                for (i = 0; i < sp.SubtitlePtr->num_rects; i++)
                {
                    SDL.SDL_Rect sub_rect = new()
                    {
                        x = sp.SubtitlePtr->rects[i]->x,
                        y = sp.SubtitlePtr->rects[i]->y,
                        w = sp.SubtitlePtr->rects[i]->w,
                        h = sp.SubtitlePtr->rects[i]->h,
                    };

                    SDL.SDL_Rect target = new()
                    {
                        x = (int)(rect.x + sub_rect.x * xratio),
                        y = (int)(rect.y + sub_rect.y * yratio),
                        w = (int)(sub_rect.w * xratio),
                        h = (int)(sub_rect.h * yratio)
                    };

                    SDL.SDL_RenderCopy(SdlRenderer, sub_texture, ref sub_rect, ref target);
                }
            }
        }

        public void Link(MediaContainer container)
        {
            Container = container;
        }

        public bool Initialize(ProgramOptions o)
        {
            if (o.display_disable)
                o.video_disable = true;

            var flags = SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_TIMER;

            if (o.audio_disable)
            {
                flags &= ~SDL.SDL_INIT_AUDIO;
            }
            else
            {
                const string AlsaBufferSizeName = "SDL_AUDIO_ALSA_SET_BUFFER_SIZE";
                /* Try to work around an occasional ALSA buffer underflow issue when the
                 * period size is NPOT due to ALSA resampling by forcing the buffer size. */
                if (Environment.GetEnvironmentVariable(AlsaBufferSizeName) == null)
                    Environment.SetEnvironmentVariable(AlsaBufferSizeName, "1", EnvironmentVariableTarget.Process);
            }

            if (o.display_disable)
                flags &= ~SDL.SDL_INIT_VIDEO;

            if (SDL.SDL_Init(flags) != 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Could not initialize SDL - {SDL.SDL_GetError()}\n");
                return false;
            }

            SDL.SDL_EventState(SDL.SDL_EventType.SDL_SYSWMEVENT, SDL.SDL_IGNORE);
            SDL.SDL_EventState(SDL.SDL_EventType.SDL_USEREVENT, SDL.SDL_IGNORE);

            if (!o.display_disable)
            {
                flags = (uint)SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN;
                if (o.alwaysontop)
                    flags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP;

                if (o.borderless)
                    flags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_BORDERLESS;
                else
                    flags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;

                RenderingWindow = SDL.SDL_CreateWindow(
                    Constants.program_name, SDL.SDL_WINDOWPOS_UNDEFINED, SDL.SDL_WINDOWPOS_UNDEFINED, default_width, default_height, (SDL.SDL_WindowFlags)flags);

                SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_SCALE_QUALITY, "linear");

                if (!RenderingWindow.IsNull())
                {
                    SdlRenderer = SDL.SDL_CreateRenderer(RenderingWindow, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);
                    if (SdlRenderer.IsNull())
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"Failed to initialize a hardware accelerated renderer: {SDL.SDL_GetError()}\n");
                        SdlRenderer = SDL.SDL_CreateRenderer(RenderingWindow, -1, 0);
                    }

                    if (!SdlRenderer.IsNull())
                    {
                        if (SDL.SDL_GetRendererInfo(SdlRenderer, out SdlRendererInfo) == 0)
                            ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Initialized {Helpers.PtrToString(SdlRendererInfo.name)} renderer.\n");
                    }
                }
                if (RenderingWindow.IsNull() || SdlRenderer.IsNull() || SdlRendererInfo.num_texture_formats <= 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Failed to create window or renderer: {SDL.SDL_GetError()}");
                    return false;
                }
            }

            return true;
        }

        public int audio_open(AudioParams wantedSpec, out AudioParams audioDeviceSpec) =>
            audio_open(wantedSpec.Layout, wantedSpec.Channels, wantedSpec.Frequency, out audioDeviceSpec);

        public int audio_open(long wantedChannelLayout, int wantedChannelCount, int wantedSampleRate, out AudioParams audioDeviceSpec)
        {
            audioDeviceSpec = new AudioParams();
            var next_nb_channels = new[] { 0, 0, 1, 6, 2, 6, 4, 6 };
            var next_sample_rates = new[] { 0, 44100, 48000, 96000, 192000 };
            int next_sample_rate_idx = next_sample_rates.Length - 1;

            const string ChannelCountEnvVariable = "SDL_AUDIO_CHANNELS";
            var env = Environment.GetEnvironmentVariable(ChannelCountEnvVariable);
            if (!string.IsNullOrWhiteSpace(env))
            {
                wantedChannelCount = int.Parse(env);
                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(wantedChannelCount);
            }

            if (wantedChannelLayout == 0 || wantedChannelCount != AudioParams.ChannelCountFor(wantedChannelLayout))
            {
                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(wantedChannelCount);
                wantedChannelLayout &= ~ffmpeg.AV_CH_LAYOUT_STEREO_DOWNMIX;
            }

            wantedChannelCount = AudioParams.ChannelCountFor(wantedChannelLayout);

            var wantedSpec = new SDL.SDL_AudioSpec
            {
                channels = (byte)wantedChannelCount,
                freq = wantedSampleRate
            };

            if (wantedSpec.freq <= 0 || wantedSpec.channels <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Invalid sample rate or channel count!\n");
                return -1;
            }

            while (next_sample_rate_idx != 0 && next_sample_rates[next_sample_rate_idx] >= wantedSpec.freq)
                next_sample_rate_idx--;

            wantedSpec.format = SDL.AUDIO_S16SYS;
            wantedSpec.silence = 0;
            wantedSpec.samples = (ushort)Math.Max(Constants.SDL_AUDIO_MIN_BUFFER_SIZE, 2 << ffmpeg.av_log2((uint)(wantedSpec.freq / Constants.SDL_AUDIO_MAX_CALLBACKS_PER_SEC)));
            wantedSpec.callback = AudioCallback;
            // wanted_spec.userdata = GCHandle.ToIntPtr(VideoStateHandle);

            const int AudioDeviceFlags = (int)(SDL.SDL_AUDIO_ALLOW_FREQUENCY_CHANGE | SDL.SDL_AUDIO_ALLOW_CHANNELS_CHANGE);
            SDL.SDL_AudioSpec deviceSpec;
            while ((AudioDeviceId = SDL.SDL_OpenAudioDevice(null, 0, ref wantedSpec, out deviceSpec, AudioDeviceFlags)) == 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"SDL_OpenAudio ({wantedSpec.channels} channels, {wantedSpec.freq} Hz): {SDL.SDL_GetError()}\n");
                wantedSpec.channels = (byte)next_nb_channels[Math.Min(7, (int)wantedSpec.channels)];
                if (wantedSpec.channels == 0)
                {
                    wantedSpec.freq = next_sample_rates[next_sample_rate_idx--];
                    wantedSpec.channels = (byte)wantedChannelCount;
                    if (wantedSpec.freq == 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "No more combinations to try, audio open failed\n");
                        return -1;
                    }
                }

                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(wantedSpec.channels);
            }

            if (deviceSpec.format != SDL.AUDIO_S16SYS)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                       $"SDL advised audio format {deviceSpec.format} is not supported!\n");
                return -1;
            }

            if (deviceSpec.channels != wantedSpec.channels)
            {
                wantedChannelLayout = AudioParams.DefaultChannelLayoutFor(deviceSpec.channels);
                if (wantedChannelLayout == 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"SDL advised channel count {deviceSpec.channels} is not supported!\n");
                    return -1;
                }
            }

            audioDeviceSpec.SampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            audioDeviceSpec.Frequency = deviceSpec.freq;
            audioDeviceSpec.Layout = wantedChannelLayout;
            audioDeviceSpec.Channels = deviceSpec.channels;

            if (audioDeviceSpec.BytesPerSecond <= 0 || audioDeviceSpec.FrameSize <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size failed\n");
                return -1;
            }

            audio_buf_index = 0;

            return (int)deviceSpec.size;
        }


        public void CloseVideo()
        {
            if (!SdlRenderer.IsNull())
                SDL.SDL_DestroyRenderer(SdlRenderer);

            if (!RenderingWindow.IsNull())
                SDL.SDL_DestroyWindow(RenderingWindow);

            if (!vid_texture.IsNull())
                SDL.SDL_DestroyTexture(vid_texture);

            if (!sub_texture.IsNull())
                SDL.SDL_DestroyTexture(sub_texture);
        }

        public void CloseAudio()
        {
            SDL.SDL_CloseAudioDevice(AudioDeviceId);
        }

        public void PauseAudio()
        {
            SDL.SDL_PauseAudioDevice(AudioDeviceId, 0);
        }

        public void video_display(MediaContainer container)
        {
            if (container.width != 0)
                video_open(container);

            _ = SDL.SDL_SetRenderDrawColor(SdlRenderer, 0, 0, 0, 255);
            _ = SDL.SDL_RenderClear(SdlRenderer);
            if (container.Audio.Stream != null && container.show_mode != ShowMode.Video)
                video_audio_display(container);
            else if (container.Video.Stream != null)
                video_image_display(container);
            SDL.SDL_RenderPresent(SdlRenderer);
        }

        private int upload_texture(ref IntPtr texture, AVFrame* frame, ref SwsContext* convertContext)
        {
            int ret = 0;
            (var sdlPixelFormat, var sdlBlendMode) = TranslateToSdlFormat((AVPixelFormat)frame->format);
            if (realloc_texture(
                ref texture,
                sdlPixelFormat == SDL.SDL_PIXELFORMAT_UNKNOWN ? SDL.SDL_PIXELFORMAT_ARGB8888 : sdlPixelFormat,
                frame->width,
                frame->height,
                sdlBlendMode,
                false) < 0)
            {
                return -1;
            }

            var textureRect = new SDL.SDL_Rect { w = frame->width, h = frame->height, x = 0, y = 0 };

            if (sdlPixelFormat == SDL.SDL_PIXELFORMAT_UNKNOWN)
            {
                /* This should only happen if we are not using avfilter... */
                convertContext = ffmpeg.sws_getCachedContext(convertContext,
                    frame->width, frame->height, (AVPixelFormat)frame->format, frame->width, frame->height,
                    AVPixelFormat.AV_PIX_FMT_BGRA, Constants.sws_flags, null, null, null);

                if (convertContext != null)
                {
                    if (SDL.SDL_LockTexture(texture, ref textureRect, out var textureAddress, out var texturePitch) == 0)
                    {
                        var targetStride = new[] { texturePitch };
                        var targetScan = default(byte_ptrArray8);
                        targetScan[0] = (byte*)textureAddress;

                        ffmpeg.sws_scale(convertContext, frame->data, frame->linesize, 0, frame->height, targetScan, targetStride);
                        SDL.SDL_UnlockTexture(texture);
                    }
                }
                else
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Cannot initialize the conversion context\n");
                    ret = -1;
                }
            }
            else if (sdlPixelFormat == SDL.SDL_PIXELFORMAT_IYUV)
            {
                if (frame->linesize[0] > 0 && frame->linesize[1] > 0 && frame->linesize[2] > 0)
                {
                    ret = SDL.SDL_UpdateYUVTexture(texture, ref textureRect, (IntPtr)frame->data[0], frame->linesize[0],
                                                           (IntPtr)frame->data[1], frame->linesize[1],
                                                           (IntPtr)frame->data[2], frame->linesize[2]);
                }
                else if (frame->linesize[0] < 0 && frame->linesize[1] < 0 && frame->linesize[2] < 0)
                {
                    ret = SDL.SDL_UpdateYUVTexture(texture, ref textureRect, (IntPtr)frame->data[0] + frame->linesize[0] * (frame->height - 1), -frame->linesize[0],
                                                           (IntPtr)frame->data[1] + frame->linesize[1] * (Helpers.AV_CEIL_RSHIFT(frame->height, 1) - 1), -frame->linesize[1],
                                                           (IntPtr)frame->data[2] + frame->linesize[2] * (Helpers.AV_CEIL_RSHIFT(frame->height, 1) - 1), -frame->linesize[2]);
                }
                else
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Mixed negative and positive linesizes are not supported.\n");
                    return -1;
                }
            }
            else
            {
                if (frame->linesize[0] < 0)
                {
                    ret = SDL.SDL_UpdateTexture(texture, ref textureRect, (IntPtr)frame->data[0] + frame->linesize[0] * (frame->height - 1), -frame->linesize[0]);
                }
                else
                {
                    ret = SDL.SDL_UpdateTexture(texture, ref textureRect, (IntPtr)frame->data[0], frame->linesize[0]);
                }
            }

            return ret;
        }

        public int video_open(MediaContainer container)
        {
            var w = screen_width != 0 ? screen_width : default_width;
            var h = screen_height != 0 ? screen_height : default_height;

            if (string.IsNullOrWhiteSpace(window_title))
                window_title = container.Options.input_filename;
            SDL.SDL_SetWindowTitle(RenderingWindow, window_title);

            SDL.SDL_SetWindowSize(RenderingWindow, w, h);
            SDL.SDL_SetWindowPosition(RenderingWindow, screen_left, screen_top);
            if (is_full_screen)
                SDL.SDL_SetWindowFullscreen(RenderingWindow, (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP);

            SDL.SDL_ShowWindow(RenderingWindow);

            container.width = w;
            container.height = h;

            return 0;
        }

        /* called to display each frame */
        public void video_refresh(MediaContainer container, ref double remaining_time)
        {
            double time;

            if (!container.IsPaused && container.MasterSyncMode == ClockSync.External && container.IsRealtime)
                container.check_external_clock_speed();

            if (!container.Options.display_disable && container.show_mode != ShowMode.Video && container.Audio.Stream != null)
            {
                time = Clock.SystemTime;
                if (force_refresh || container.last_vis_time + rdftspeed < time)
                {
                    video_display(container);
                    container.last_vis_time = time;
                }
                remaining_time = Math.Min(remaining_time, container.last_vis_time + rdftspeed - time);
            }

            if (container.Video.Stream != null)
            {
            retry:
                if (container.Video.Frames.PendingCount == 0)
                {
                    // nothing to do, no picture to display in the queue
                }
                else
                {
                    /* dequeue the picture */
                    var lastvp = container.Video.Frames.PeekLast();
                    var vp = container.Video.Frames.Peek();

                    if (vp.Serial != container.Video.Packets.Serial)
                    {
                        container.Video.Frames.Next();
                        goto retry;
                    }

                    if (lastvp.Serial != vp.Serial)
                        container.frame_timer = Clock.SystemTime;

                    if (container.IsPaused)
                        goto display;

                    /* compute nominal last_duration */
                    var last_duration = vp_duration(container, lastvp, vp);
                    var delay = compute_target_delay(last_duration, container);

                    time = Clock.SystemTime;
                    if (time < container.frame_timer + delay)
                    {
                        remaining_time = Math.Min(container.frame_timer + delay - time, remaining_time);
                        goto display;
                    }

                    container.frame_timer += delay;
                    if (delay > 0 && time - container.frame_timer > Constants.AV_SYNC_THRESHOLD_MAX)
                        container.frame_timer = time;

                    if (vp.HasValidTime)
                        update_video_pts(container, vp.Time, vp.Position, vp.Serial);

                    if (container.Video.Frames.PendingCount > 1)
                    {
                        var nextvp = container.Video.Frames.PeekNext();
                        var duration = vp_duration(container, vp, nextvp);
                        if (container.step == false &&
                            (container.Options.framedrop > 0 ||
                            (container.Options.framedrop != 0 && container.MasterSyncMode != ClockSync.Video))
                            && time > container.frame_timer + duration)
                        {
                            container.frame_drops_late++;
                            container.Video.Frames.Next();
                            goto retry;
                        }
                    }

                    if (container.Subtitle.Stream != null)
                    {
                        while (container.Subtitle.Frames.PendingCount > 0)
                        {
                            var sp = container.Subtitle.Frames.Peek();
                            var sp2 = container.Subtitle.Frames.PendingCount > 1
                                ? container.Subtitle.Frames.PeekNext()
                                : null;

                            if (sp.Serial != container.Subtitle.Packets.Serial
                                    || (container.VideoClock.Pts > (sp.Time + ((float)sp.SubtitlePtr->end_display_time / 1000)))
                                    || (sp2 != null && container.VideoClock.Pts > (sp2.Time + ((float)sp2.SubtitlePtr->start_display_time / 1000))))
                            {
                                if (sp.uploaded)
                                {
                                    for (var i = 0; i < sp.SubtitlePtr->num_rects; i++)
                                    {
                                        var sub_rect = new SDL.SDL_Rect()
                                        {
                                            x = sp.SubtitlePtr->rects[i]->x,
                                            y = sp.SubtitlePtr->rects[i]->y,
                                            w = sp.SubtitlePtr->rects[i]->w,
                                            h = sp.SubtitlePtr->rects[i]->h,
                                        };

                                        if (SDL.SDL_LockTexture(sub_texture, ref sub_rect, out var pixels, out var pitch) == 0)
                                        {
                                            var ptr = (byte*)pixels;
                                            for (var j = 0; j < sub_rect.h; j++, ptr += pitch)
                                            {
                                                for (var b = 0; b < sub_rect.w << 2; b++)
                                                {
                                                    ptr[b] = byte.MinValue;
                                                }
                                            }

                                            SDL.SDL_UnlockTexture(sub_texture);
                                        }
                                    }
                                }
                                container.Subtitle.Frames.Next();
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    container.Video.Frames.Next();
                    force_refresh = true;

                    if (container.step && !container.IsPaused)
                        container.stream_toggle_pause();
                }
            display:
                /* display picture */
                if (!container.Options.display_disable && force_refresh && container.show_mode == ShowMode.Video && container.Video.Frames.IsReadIndexShown)
                    video_display(container);
            }

            force_refresh = false;
            if (container.Options.show_status != 0)
            {
                int aqsize, vqsize, sqsize;

                var currentTime = Clock.SystemTime;
                if (last_time_status == 0 || (currentTime - last_time_status) >= 0.03)
                {
                    aqsize = container.Audio.Stream != null ? container.Audio.Packets.Size : 0;
                    vqsize = container.Video.Stream != null ? container.Video.Packets.Size : 0;
                    sqsize = container.Subtitle.Stream != null ? container.Subtitle.Packets.Size : 0;

                    var av_diff = 0d;
                    if (container.Audio.Stream != null && container.Video.Stream != null)
                        av_diff = container.AudioClock.Time - container.VideoClock.Time;
                    else if (container.Video.Stream != null)
                        av_diff = container.MasterTime - container.VideoClock.Time;
                    else if (container.Audio.Stream != null)
                        av_diff = container.MasterTime - container.AudioClock.Time;

                    var buf = new StringBuilder();
                    buf.Append($"{container.MasterTime,-8:0.####} ");
                    buf.Append((container.Audio.Stream != null && container.Video.Stream != null) ? "A-V" : (container.Video.Stream != null ? "M-V" : (container.Audio.Stream != null ? "M-A" : "   ")));
                    buf.Append($":{av_diff,-8:0.####} ");
                    buf.Append($"fd={(container.frame_drops_early + container.frame_drops_late)} ");
                    buf.Append($"aq={(aqsize / 1024)}KB ");
                    buf.Append($"vq={(vqsize / 1024)}KB ");
                    buf.Append($"sq={(sqsize)}B ");
                    buf.Append($" f={(container.Video.Stream != null ? container.Video.CodecContext->pts_correction_num_faulty_dts : 0)} / ");
                    buf.Append($"{(container.Video.Stream != null ? container.Video.CodecContext->pts_correction_num_faulty_pts : 0)}");

                    if (container.Options.show_status == 1 && ffmpeg.av_log_get_level() < ffmpeg.AV_LOG_INFO)
                        Console.WriteLine(buf.ToString());
                    else
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"{buf}\n");

                    last_time_status = currentTime;
                }
            }
        }

        public void toggle_full_screen()
        {
            is_full_screen = !is_full_screen;
            SDL.SDL_SetWindowFullscreen(RenderingWindow, (uint)(is_full_screen ? SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP : 0));
        }

        static SDL.SDL_Rect calculate_display_rect(
            int screenX, int screenY, int screenWidth, int screenHeight, int pictureWidth, int pictureHeight, AVRational pictureAspectRatio)
        {
            var aspectRatio = pictureAspectRatio;
            if (ffmpeg.av_cmp_q(aspectRatio, ffmpeg.av_make_q(0, 1)) <= 0)
                aspectRatio = ffmpeg.av_make_q(1, 1);

            aspectRatio = ffmpeg.av_mul_q(aspectRatio, ffmpeg.av_make_q(pictureWidth, pictureHeight));

            // TODO: we suppose the screen has a 1.0 pixel ratio
            long height = screenHeight;
            long width = ffmpeg.av_rescale(height, aspectRatio.num, aspectRatio.den) & ~1;
            if (width > screenWidth)
            {
                width = screenWidth;
                height = ffmpeg.av_rescale(width, aspectRatio.den, aspectRatio.num) & ~1;
            }

            var x = (screenWidth - width) / 2;
            var y = (screenHeight - height) / 2;

            return new SDL.SDL_Rect
            {
                x = screenX + (int)x,
                y = screenY + (int)y,
                w = Math.Max((int)width, 1),
                h = Math.Max((int)height, 1),
            };
        }

        public void set_sdl_yuv_conversion_mode(AVFrame* frame)
        {
        }

        public void video_audio_display(MediaContainer s)
        {
        }

        private static (uint sdlPixelFormat, SDL.SDL_BlendMode sdlBlendMode) TranslateToSdlFormat(AVPixelFormat pixelFormat)
        {
            var sdlBlendMode = SDL.SDL_BlendMode.SDL_BLENDMODE_NONE;
            var sdlPixelFormat = SDL.SDL_PIXELFORMAT_UNKNOWN;
            if (pixelFormat == AVPixelFormat.AV_PIX_FMT_RGBA ||
                pixelFormat == AVPixelFormat.AV_PIX_FMT_ARGB ||
                pixelFormat == AVPixelFormat.AV_PIX_FMT_BGRA ||
                pixelFormat == AVPixelFormat.AV_PIX_FMT_ABGR)
                sdlBlendMode = SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND;

            if (sdl_texture_map.ContainsKey(pixelFormat))
                sdlPixelFormat = sdl_texture_map[pixelFormat];

            return (sdlPixelFormat, sdlBlendMode);
        }

        public void set_default_window_size(int width, int height, AVRational sar)
        {
            var maxWidth = screen_width != 0 ? screen_width : int.MaxValue;
            var maxHeight = screen_height != 0 ? screen_height : int.MaxValue;
            if (maxWidth == int.MaxValue && maxHeight == int.MaxValue)
                maxHeight = height;

            var rect = calculate_display_rect(0, 0, maxWidth, maxHeight, width, height, sar);
            default_width = rect.w;
            default_height = rect.h;
        }

        /* copy samples for viewing in editor window */
        static void update_sample_display(MediaContainer container, short* samples, int samples_size)
        {
            int size, len;

            size = samples_size / sizeof(short);
            while (size > 0)
            {
                len = Constants.SAMPLE_ARRAY_SIZE - container.sample_array_index;
                if (len > size)
                    len = size;

                var targetAddress = &container.sample_array[container.sample_array_index];
                Buffer.MemoryCopy(samples, targetAddress, len * sizeof(short), len * sizeof(short));

                samples += len;
                container.sample_array_index += len;
                if (container.sample_array_index >= Constants.SAMPLE_ARRAY_SIZE)
                    container.sample_array_index = 0;
                size -= len;
            }
        }

        /* prepare a new audio buffer */
        public void sdl_audio_callback(IntPtr opaque, IntPtr stream, int len)
        {
            audio_callback_time = Clock.SystemTime;

            while (len > 0)
            {
                if (audio_buf_index >= Container.audio_buf_size)
                {
                    var audio_size = Container.Audio.audio_decode_frame();
                    if (audio_size < 0)
                    {
                        /* if error, just output silence */
                        Container.audio_buf = null;
                        Container.audio_buf_size = (uint)(Constants.SDL_AUDIO_MIN_BUFFER_SIZE / Container.Audio.HardwareSpec.FrameSize * Container.Audio.HardwareSpec.FrameSize);
                    }
                    else
                    {
                        if (Container.show_mode != ShowMode.Video)
                            update_sample_display(Container, (short*)Container.audio_buf, audio_size);
                        Container.audio_buf_size = (uint)audio_size;
                    }
                    audio_buf_index = 0;
                }
                var len1 = (int)(Container.audio_buf_size - audio_buf_index);
                if (len1 > len)
                    len1 = len;

                if (!Container.IsMuted && Container.audio_buf != null && audio_volume == SDL.SDL_MIX_MAXVOLUME)
                {
                    var dest = (byte*)stream;
                    var source = (Container.audio_buf + audio_buf_index);
                    for (var b = 0; b < len1; b++)
                        dest[b] = source[b];
                }
                else
                {
                    var target = (byte*)stream;
                    for (var b = 0; b < len1; b++)
                        target[b] = 0;

                    if (!Container.IsMuted && Container.audio_buf != null)
                        SDL.SDL_MixAudioFormat((byte*)stream, Container.audio_buf + audio_buf_index, SDL.AUDIO_S16SYS, (uint)len1, audio_volume);
                }

                len -= len1;
                stream += len1;
                audio_buf_index += len1;
            }

            audio_write_buf_size = (int)(Container.audio_buf_size - audio_buf_index);
            /* Let's assume the audio driver that is used by SDL has two periods. */
            if (!Container.Audio.FrameTime.IsNaN())
            {
                Container.AudioClock.Set(Container.Audio.FrameTime - (double)(2 * Container.audio_hw_buf_size + audio_write_buf_size) / Container.Audio.HardwareSpec.BytesPerSecond, Container.audio_clock_serial, audio_callback_time);
                Container.ExternalClock.SyncToSlave(Container.AudioClock);
            }
        }

        public void update_volume(MediaContainer container, int sign, double step)
        {
            var volume_level = audio_volume > 0 ? (20 * Math.Log(audio_volume / (double)SDL.SDL_MIX_MAXVOLUME) / Math.Log(10)) : -1000.0;
            var new_volume = (int)Math.Round(SDL.SDL_MIX_MAXVOLUME * Math.Pow(10.0, (volume_level + sign * step) / 20.0), 0);
            audio_volume = Helpers.av_clip(audio_volume == new_volume ? (audio_volume + sign) : new_volume, 0, SDL.SDL_MIX_MAXVOLUME);
        }

        static double compute_target_delay(double delay, MediaContainer container)
        {
            var diff = 0d;

            /* update delay to follow master synchronisation source */
            if (container.MasterSyncMode != ClockSync.Video)
            {
                /* if video is slave, we try to correct big delays by
                   duplicating or deleting a frame */
                diff = container.VideoClock.Time - container.MasterTime;

                /* skip or repeat frame. We take into account the
                   delay to compute the threshold. I still don't know
                   if it is the best guess */
                var syncThreshold = Math.Max(Constants.AV_SYNC_THRESHOLD_MIN, Math.Min(Constants.AV_SYNC_THRESHOLD_MAX, delay));
                if (!diff.IsNaN() && Math.Abs(diff) < container.max_frame_duration)
                {
                    if (diff <= -syncThreshold)
                        delay = Math.Max(0, delay + diff);
                    else if (diff >= syncThreshold && delay > Constants.AV_SYNC_FRAMEDUP_THRESHOLD)
                        delay += diff;
                    else if (diff >= syncThreshold)
                        delay = 2 * delay;
                }
            }

            ffmpeg.av_log(null, ffmpeg.AV_LOG_TRACE, $"video: delay={delay,-8:0.####} A-V={-diff,-8:0.####}\n");

            return delay;
        }

        static double vp_duration(MediaContainer container, FrameHolder currentFrame, FrameHolder nextFrame)
        {
            if (currentFrame.Serial == nextFrame.Serial)
            {
                var duration = nextFrame.Time - currentFrame.Time;
                if (duration.IsNaN() || duration <= 0 || duration > container.max_frame_duration)
                    return currentFrame.Duration;
                else
                    return duration;
            }
            else
            {
                return 0.0;
            }
        }

        static void update_video_pts(MediaContainer container, double pts, long pos, int serial)
        {
            /* update current video pts */
            container.VideoClock.Set(pts, serial);
            container.ExternalClock.SyncToSlave(container.VideoClock);
        }
    }
}
