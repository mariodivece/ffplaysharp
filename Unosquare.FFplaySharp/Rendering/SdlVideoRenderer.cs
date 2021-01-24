namespace Unosquare.FFplaySharp.Rendering
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe class SdlVideoRenderer : IVideoRenderer
    {
        private int DroppedPictureCount;
        private int default_width = 640;
        private int default_height = 480;

        private IntPtr RenderingWindow;
        private IntPtr SdlRenderer;
        public SDL.SDL_RendererInfo SdlRendererInfo;

        public string WindowTitle { get; set; }

        private IntPtr sub_texture;
        private IntPtr vid_texture;

        private int screen_left = SDL.SDL_WINDOWPOS_CENTERED;
        private int screen_top = SDL.SDL_WINDOWPOS_CENTERED;
        private bool is_full_screen;

        public int screen_width { get; set; } = 0;
        public int screen_height { get; set; } = 0;

        public bool ForceRefresh { get; set; }

        // inlined static variables
        public double last_time_status = 0;

        public MediaContainer Container => Presenter.Container;

        public IPresenter Presenter { get; private set; }

        public static readonly Dictionary<AVPixelFormat, uint> sdl_texture_map = new()
        {
            { AVPixelFormat.AV_PIX_FMT_RGB8, SDL.SDL_PIXELFORMAT_RGB332 },
            { Constants.AV_PIX_FMT_RGB444, SDL.SDL_PIXELFORMAT_RGB444 },
            { Constants.AV_PIX_FMT_RGB555, SDL.SDL_PIXELFORMAT_RGB555 },
            { Constants.AV_PIX_FMT_BGR555, SDL.SDL_PIXELFORMAT_BGR555 },
            { Constants.AV_PIX_FMT_RGB565, SDL.SDL_PIXELFORMAT_RGB565 },
            { Constants.AV_PIX_FMT_BGR565, SDL.SDL_PIXELFORMAT_BGR565 },
            { AVPixelFormat.AV_PIX_FMT_RGB24, SDL.SDL_PIXELFORMAT_RGB24 },
            { AVPixelFormat.AV_PIX_FMT_BGR24, SDL.SDL_PIXELFORMAT_BGR24 },
            { Constants.AV_PIX_FMT_0RGB32, SDL.SDL_PIXELFORMAT_RGB888 },
            { Constants.AV_PIX_FMT_0BGR32, SDL.SDL_PIXELFORMAT_BGR888 },
            { Constants.AV_PIX_FMT_0BGRLE, SDL.SDL_PIXELFORMAT_RGBX8888 },
            { Constants.AV_PIX_FMT_0RGBLE, SDL.SDL_PIXELFORMAT_BGRX8888 },
            { Constants.AV_PIX_FMT_RGB32, SDL.SDL_PIXELFORMAT_ARGB8888 },
            { Constants.AV_PIX_FMT_RGB32_1, SDL.SDL_PIXELFORMAT_RGBA8888 },
            { Constants.AV_PIX_FMT_BGR32, SDL.SDL_PIXELFORMAT_ABGR8888 },
            { Constants.AV_PIX_FMT_BGR32_1, SDL.SDL_PIXELFORMAT_BGRA8888 },
            { AVPixelFormat.AV_PIX_FMT_YUV420P, SDL.SDL_PIXELFORMAT_IYUV },
            { AVPixelFormat.AV_PIX_FMT_YUYV422, SDL.SDL_PIXELFORMAT_YUY2 },
            { AVPixelFormat.AV_PIX_FMT_UYVY422, SDL.SDL_PIXELFORMAT_UYVY },
            { AVPixelFormat.AV_PIX_FMT_NONE, SDL.SDL_PIXELFORMAT_UNKNOWN },
        };

        public void Initialize(IPresenter presenter)
        {
            Presenter = presenter;

            var parent = Presenter as SdlPresenter;
            var o = Presenter.Container.Options;
            if (o.display_disable) return;

            parent.SdlInitFlags = (uint)SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN;
            if (o.alwaysontop)
                parent.SdlInitFlags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP;

            if (o.borderless)
                parent.SdlInitFlags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_BORDERLESS;
            else
                parent.SdlInitFlags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;

            RenderingWindow = SDL.SDL_CreateWindow(
                Constants.program_name, SDL.SDL_WINDOWPOS_UNDEFINED, SDL.SDL_WINDOWPOS_UNDEFINED, default_width, default_height, (SDL.SDL_WindowFlags)parent.SdlInitFlags);

            SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_SCALE_QUALITY, "best");

            if (!RenderingWindow.IsNull())
            {
                SdlRenderer = SDL.SDL_CreateRenderer(RenderingWindow, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);
                if (SdlRenderer.IsNull())
                {
                    Helpers.LogWarning($"Failed to initialize a hardware accelerated renderer: {SDL.SDL_GetError()}\n");
                    SdlRenderer = SDL.SDL_CreateRenderer(RenderingWindow, -1, 0);
                }

                if (!SdlRenderer.IsNull())
                {
                    if (SDL.SDL_GetRendererInfo(SdlRenderer, out SdlRendererInfo) == 0)
                        Helpers.LogVerbose($"Initialized {Helpers.PtrToString(SdlRendererInfo.name)} renderer.\n");
                }
            }
            if (RenderingWindow.IsNull() || SdlRenderer.IsNull() || SdlRendererInfo.num_texture_formats <= 0)
            {
                var errorMessage = $"Failed to create window or renderer: {SDL.SDL_GetError()}";
                Helpers.LogFatal(errorMessage);
                throw new Exception(errorMessage);
            }
        }

        public IEnumerable<AVPixelFormat> RetrieveSupportedPixelFormats()
        {
            var outputPixelFormats = new List<AVPixelFormat>(sdl_texture_map.Count);
            for (var i = 0; i < SdlRendererInfo.num_texture_formats; i++)
            {
                foreach (var kvp in sdl_texture_map)
                {
                    if (kvp.Value == SdlRendererInfo.texture_formats[i])
                        outputPixelFormats.Add(kvp.Key);
                }
            }

            return outputPixelFormats;
        }

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

                Helpers.LogVerbose($"Created {new_width}x{new_height} texture with {SDL.SDL_GetPixelFormatName(new_format)}.\n");
            }
            return 0;
        }

        public void video_image_display(MediaContainer container)
        {
            FrameHolder subtitleFrame = null;
            var videoFrame = container.Video.Frames.PeekLast();

            if (container.HasSubtitles && container.Subtitle.Frames.PendingCount > 0)
            {
                subtitleFrame = container.Subtitle.Frames.Peek();

                if (videoFrame.Time >= subtitleFrame.StartDisplayTime)
                {
                    if (!subtitleFrame.IsUploaded)
                    {
                        if (subtitleFrame.Width <= 0 || subtitleFrame.Height <= 0)
                            subtitleFrame.UpdateDimensions(videoFrame.Width, videoFrame.Height);

                        if (realloc_texture(ref sub_texture, SDL.SDL_PIXELFORMAT_ARGB8888, subtitleFrame.Width, subtitleFrame.Height, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND, true) < 0)
                            return;

                        for (var i = 0; i < subtitleFrame.SubtitlePtr->num_rects; i++)
                        {
                            var targetRect = CreateRect(subtitleFrame.SubtitlePtr->rects[i], subtitleFrame);

                            container.Subtitle.ConvertContext = ffmpeg.sws_getCachedContext(
                                container.Subtitle.ConvertContext,
                                targetRect.w, targetRect.h, AVPixelFormat.AV_PIX_FMT_PAL8,
                                targetRect.w, targetRect.h, AVPixelFormat.AV_PIX_FMT_BGRA,
                                0, null, null, null);

                            if (container.Subtitle.ConvertContext == null)
                            {
                                Helpers.LogFatal("Cannot initialize the conversion context\n");
                                return;
                            }

                            if (SDL.SDL_LockTexture(sub_texture, ref targetRect, out var pixels, out var pitch) == 0)
                            {
                                var targetStride = new[] { pitch };
                                var targetScan = default(byte_ptrArray8);
                                targetScan[0] = (byte*)pixels;

                                ffmpeg.sws_scale(container.Subtitle.ConvertContext,
                                    subtitleFrame.SubtitlePtr->rects[i]->data,
                                    subtitleFrame.SubtitlePtr->rects[i]->linesize,
                                    0,
                                    subtitleFrame.SubtitlePtr->rects[i]->h,
                                    targetScan,
                                    targetStride);

                                SDL.SDL_UnlockTexture(sub_texture);
                            }
                        }

                        subtitleFrame.MarkUploaded();
                    }
                }
                else
                {
                    subtitleFrame = null;
                }

            }

            var rect = CalculateDisplayRect(container.xleft, container.ytop, container.width, container.height, videoFrame.Width, videoFrame.Height, videoFrame.Sar);

            if (!videoFrame.IsUploaded)
            {
                if (upload_texture(ref vid_texture, videoFrame, ref container.Video.ConvertContext) < 0)
                    return;
                videoFrame.MarkUploaded();
                videoFrame.FlipVertical = videoFrame.FramePtr->linesize[0] < 0;
            }

            var point = new SDL.SDL_Point();
            SDL.SDL_RenderCopyEx(SdlRenderer, vid_texture, ref rect, ref rect, 0, ref point, videoFrame.FlipVertical ? SDL.SDL_RendererFlip.SDL_FLIP_VERTICAL : SDL.SDL_RendererFlip.SDL_FLIP_NONE);

            if (subtitleFrame != null)
            {
                int i;
                double xratio = (double)rect.w / (double)subtitleFrame.Width;
                double yratio = (double)rect.h / (double)subtitleFrame.Height;
                for (i = 0; i < subtitleFrame.SubtitlePtr->num_rects; i++)
                {
                    SDL.SDL_Rect sub_rect = new()
                    {
                        x = subtitleFrame.SubtitlePtr->rects[i]->x,
                        y = subtitleFrame.SubtitlePtr->rects[i]->y,
                        w = subtitleFrame.SubtitlePtr->rects[i]->w,
                        h = subtitleFrame.SubtitlePtr->rects[i]->h,
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

        public void Close()
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


        public void video_display()
        {
            if (Container.width <= 0)
                video_open();

            _ = SDL.SDL_SetRenderDrawColor(SdlRenderer, 0, 0, 0, 255);
            _ = SDL.SDL_RenderClear(SdlRenderer);

            if (Container.HasVideo)
                video_image_display(Container);

            SDL.SDL_RenderPresent(SdlRenderer);
        }

        private static SDL.SDL_Rect CreateRect(AVSubtitleRect* rect, FrameHolder targetFrame)
        {
            var result = CreateRect(rect);
            result.x = result.x.Clamp(0, targetFrame.Width);
            result.y = result.y.Clamp(0, targetFrame.Height);
            result.w = result.w.Clamp(0, targetFrame.Width - result.x);
            result.h = result.h.Clamp(0, targetFrame.Height - result.y);

            return result;
        }

        private static SDL.SDL_Rect CreateRect(AVSubtitleRect* rect)
        {
            return new SDL.SDL_Rect
            {
                x = rect->x,
                y = rect->y,
                w = rect->w,
                h = rect->h
            };
        }

        private static SDL.SDL_Rect CreateRect(FrameHolder frame)
        {
            return new SDL.SDL_Rect { w = frame.Width, h = frame.Height, x = 0, y = 0 };
        }

        private int upload_texture(ref IntPtr texture, FrameHolder frame, ref SwsContext* convertContext)
        {
            var resultCode = 0;
            (var sdlPixelFormat, var sdlBlendMode) = TranslateToSdlFormat(frame.PixelFormat);
            var textureFormat = sdlPixelFormat == SDL.SDL_PIXELFORMAT_UNKNOWN ? SDL.SDL_PIXELFORMAT_ARGB8888 : sdlPixelFormat;

            if (realloc_texture(ref texture, textureFormat, frame.Width, frame.Height, sdlBlendMode, false) < 0)
                return -1;

            var textureRect = CreateRect(frame);

            if (sdlPixelFormat == SDL.SDL_PIXELFORMAT_UNKNOWN)
            {
                /* This should only happen if we are not using avfilter... */
                convertContext = ffmpeg.sws_getCachedContext(convertContext,
                    frame.Width, frame.Height, frame.PixelFormat, frame.Width, frame.Height,
                    AVPixelFormat.AV_PIX_FMT_BGRA, Constants.sws_flags, null, null, null);

                if (convertContext != null)
                {
                    if (SDL.SDL_LockTexture(texture, ref textureRect, out var textureAddress, out var texturePitch) == 0)
                    {
                        var targetStride = new[] { texturePitch };
                        var targetScan = default(byte_ptrArray8);
                        targetScan[0] = (byte*)textureAddress;

                        ffmpeg.sws_scale(convertContext, frame.PixelData, frame.PixelStride, 0, frame.Height, targetScan, targetStride);
                        SDL.SDL_UnlockTexture(texture);
                    }
                }
                else
                {
                    Helpers.LogFatal("Cannot initialize the conversion context\n");
                    resultCode = -1;
                }
            }
            else if (sdlPixelFormat == SDL.SDL_PIXELFORMAT_IYUV)
            {
                if (frame.PixelStride[0] > 0 && frame.PixelStride[1] > 0 && frame.PixelStride[2] > 0)
                {
                    resultCode = SDL.SDL_UpdateYUVTexture(texture, ref textureRect, (IntPtr)frame.PixelData[0], frame.PixelStride[0],
                                                           (IntPtr)frame.PixelData[1], frame.PixelStride[1],
                                                           (IntPtr)frame.PixelData[2], frame.PixelStride[2]);
                }
                else if (frame.PixelStride[0] < 0 && frame.PixelStride[1] < 0 && frame.PixelStride[2] < 0)
                {
                    resultCode = SDL.SDL_UpdateYUVTexture(texture, ref textureRect, (IntPtr)frame.PixelData[0] + frame.PixelStride[0] * (frame.Height - 1), -frame.PixelStride[0],
                                                           (IntPtr)frame.PixelData[1] + frame.PixelStride[1] * (Helpers.AV_CEIL_RSHIFT(frame.Height, 1) - 1), -frame.PixelStride[1],
                                                           (IntPtr)frame.PixelData[2] + frame.PixelStride[2] * (Helpers.AV_CEIL_RSHIFT(frame.Height, 1) - 1), -frame.PixelStride[2]);
                }
                else
                {
                    Helpers.LogError("Mixed negative and positive linesizes are not supported.\n");
                    return -1;
                }
            }
            else
            {
                if (frame.PixelStride[0] < 0)
                {
                    resultCode = SDL.SDL_UpdateTexture(texture, ref textureRect, (IntPtr)frame.PixelData[0] + frame.PixelStride[0] * (frame.Height - 1), -frame.PixelStride[0]);
                }
                else
                {
                    resultCode = SDL.SDL_UpdateTexture(texture, ref textureRect, (IntPtr)frame.PixelData[0], frame.PixelStride[0]);
                }
            }

            return resultCode;
        }

        public int video_open()
        {
            var w = screen_width != 0 ? screen_width : default_width;
            var h = screen_height != 0 ? screen_height : default_height;

            if (string.IsNullOrWhiteSpace(WindowTitle))
                WindowTitle = Container.Options.input_filename;
            SDL.SDL_SetWindowTitle(RenderingWindow, WindowTitle);

            SDL.SDL_SetWindowSize(RenderingWindow, w, h);
            SDL.SDL_SetWindowPosition(RenderingWindow, screen_left, screen_top);
            if (is_full_screen)
                SDL.SDL_SetWindowFullscreen(RenderingWindow, (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP);

            SDL.SDL_ShowWindow(RenderingWindow);

            Container.width = w;
            Container.height = h;

            return 0;
        }

        /* called to display each frame */
        public void Present(ref double remainingTime)
        {
            if (!Container.IsPaused && Container.MasterSyncMode == ClockSync.External && Container.IsRealtime)
                Container.SyncExternalClockSpeed();

            if (Container.HasVideo)
            {
            retry:
                if (Container.Video.Frames.PendingCount == 0)
                {
                    // nothing to do, no picture to display in the queue
                }
                else
                {
                    /* dequeue the picture */
                    var previousPicture = Container.Video.Frames.PeekLast();
                    var currentPicture = Container.Video.Frames.Peek();

                    if (currentPicture.Serial != Container.Video.Packets.Serial)
                    {
                        Container.Video.Frames.Next();
                        goto retry;
                    }

                    if (previousPicture.Serial != currentPicture.Serial)
                        Container.PictureDisplayTimer = Clock.SystemTime;

                    if (Container.IsPaused)
                        goto display;

                    /* compute nominal last_duration */
                    var pictureDuration = ComputePictureDuration(Container, previousPicture, currentPicture);
                    var pictureDisplayDuration = ComputePictureDisplayDuration(pictureDuration, Container);

                    var currentTime = Clock.SystemTime;
                    if (currentTime < Container.PictureDisplayTimer + pictureDisplayDuration)
                    {
                        remainingTime = Math.Min(Container.PictureDisplayTimer + pictureDisplayDuration - currentTime, remainingTime);
                        goto display;
                    }

                    Container.PictureDisplayTimer += pictureDisplayDuration;
                    if (pictureDisplayDuration > 0 && currentTime - Container.PictureDisplayTimer > Constants.AV_SYNC_THRESHOLD_MAX)
                        Container.PictureDisplayTimer = currentTime;

                    if (currentPicture.HasValidTime)
                        update_video_pts(Container, currentPicture.Time, currentPicture.Serial);

                    if (Container.Video.Frames.PendingCount > 1)
                    {
                        var nextPicture = Container.Video.Frames.PeekNext();
                        var duration = ComputePictureDuration(Container, currentPicture, nextPicture);
                        if (Container.IsInStepMode == false &&
                            (Container.Options.framedrop > 0 ||
                            (Container.Options.framedrop != 0 && Container.MasterSyncMode != ClockSync.Video)) &&
                            currentTime > Container.PictureDisplayTimer + duration)
                        {
                            DroppedPictureCount++;
                            Container.Video.Frames.Next();
                            goto retry;
                        }
                    }

                    if (Container.HasSubtitles)
                    {
                        while (Container.Subtitle.Frames.PendingCount > 0)
                        {
                            var sp = Container.Subtitle.Frames.Peek();
                            var sp2 = Container.Subtitle.Frames.PendingCount > 1
                                ? Container.Subtitle.Frames.PeekNext()
                                : null;

                            if (sp.Serial != Container.Subtitle.Packets.Serial
                                    || (Container.VideoClock.BaseTime > sp.EndDisplayTime)
                                    || (sp2 != null && Container.VideoClock.BaseTime > sp2.StartDisplayTime))
                            {
                                if (sp.IsUploaded)
                                {
                                    for (var i = 0; i < sp.SubtitlePtr->num_rects; i++)
                                    {
                                        var sub_rect = CreateRect(sp.SubtitlePtr->rects[i]);

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
                                Container.Subtitle.Frames.Next();
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    Container.Video.Frames.Next();
                    ForceRefresh = true;

                    if (Container.IsInStepMode && !Container.IsPaused)
                        Container.StreamTogglePause();
                }
            display:
                /* display picture */
                if (!Container.Options.display_disable && ForceRefresh && Container.ShowMode == ShowMode.Video && Container.Video.Frames.IsReadIndexShown)
                    video_display();
            }

            ForceRefresh = false;
            if (Container.Options.show_status != 0)
            {
                var currentTime = Clock.SystemTime;
                if (last_time_status == 0 || (currentTime - last_time_status) >= 0.03)
                {
                    var audioQueueSize = Container.HasAudio ? Container.Audio.Packets.Size : 0;
                    var videoQueueSize = Container.HasVideo ? Container.Video.Packets.Size : 0;
                    var subtitleQueueSize = Container.HasSubtitles ? Container.Subtitle.Packets.Size : 0;

                    var audioVideoDelay = Container.ComponentSyncDelay;

                    var buf = new StringBuilder();
                    buf.Append($"{Container.MasterTime,-8:0.####} | ");
                    buf.Append((Container.HasAudio && Container.HasVideo) ? "A-V" : (Container.HasVideo ? "M-V" : (Container.HasAudio ? "M-A" : "   ")));
                    buf.Append($":{audioVideoDelay,9:0.####} | ");
                    buf.Append($"fd={(Container.Video.DroppedFrameCount + DroppedPictureCount)} | ");
                    buf.Append($"aq={(audioQueueSize / 1024)}KB | ");
                    buf.Append($"vq={(videoQueueSize / 1024)}KB | ");
                    buf.Append($"sq={subtitleQueueSize}B | ");
                    buf.Append($" f={(Container.HasVideo ? Container.Video.CodecContext->pts_correction_num_faulty_dts : 0)} / ");
                    buf.Append($"{(Container.HasVideo ? Container.Video.CodecContext->pts_correction_num_faulty_pts : 0)}");

                    for (var i = buf.Length; i < 90; i++)
                        buf.Append(' ');

                    if (Container.Options.show_status == 1 && ffmpeg.av_log_get_level() < ffmpeg.AV_LOG_INFO)
                        Console.Write($"{buf}\r");
                    else
                        Helpers.LogInfo($"{buf}\r");

                    last_time_status = currentTime;
                }
            }
        }

        public void ToggleFullScreen()
        {
            is_full_screen = !is_full_screen;
            SDL.SDL_SetWindowFullscreen(RenderingWindow, (uint)(is_full_screen ? SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP : 0));
        }

        private static SDL.SDL_Rect CalculateDisplayRect(
            int screenX, int screenY, int screenWidth, int screenHeight, int pictureWidth, int pictureHeight, AVRational pictureAspectRatio)
        {
            var aspectRatio = pictureAspectRatio;
            if (ffmpeg.av_cmp_q(aspectRatio, ffmpeg.av_make_q(0, 1)) <= 0)
                aspectRatio = ffmpeg.av_make_q(1, 1);

            aspectRatio = ffmpeg.av_mul_q(aspectRatio, ffmpeg.av_make_q(pictureWidth, pictureHeight));

            // TODO: we suppose the screen has a 1.0 pixel ratio
            var height = (long)screenHeight;
            var width = ffmpeg.av_rescale(height, aspectRatio.num, aspectRatio.den) & ~1;
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

            var rect = CalculateDisplayRect(0, 0, maxWidth, maxHeight, width, height, sar);
            default_width = rect.w;
            default_height = rect.h;
        }

        static double ComputePictureDisplayDuration(double pictureDuration, MediaContainer container)
        {
            var clockDifference = 0d;

            /* update delay to follow master synchronisation source */
            if (container.MasterSyncMode != ClockSync.Video)
            {
                /* if video is slave, we try to correct big delays by
                   duplicating or deleting a frame */
                clockDifference = container.VideoClock.Value - container.MasterTime;

                /* skip or repeat frame. We take into account the
                   delay to compute the threshold. I still don't know
                   if it is the best guess */
                var syncThreshold = Math.Max(Constants.AV_SYNC_THRESHOLD_MIN, Math.Min(Constants.AV_SYNC_THRESHOLD_MAX, pictureDuration));
                if (!clockDifference.IsNaN() && Math.Abs(clockDifference) < container.MaxPictureDuration)
                {
                    if (clockDifference <= -syncThreshold)
                        pictureDuration = Math.Max(0, pictureDuration + clockDifference);
                    else if (clockDifference >= syncThreshold && pictureDuration > Constants.AV_SYNC_FRAMEDUP_THRESHOLD)
                        pictureDuration += clockDifference;
                    else if (clockDifference >= syncThreshold)
                        pictureDuration = 2 * pictureDuration;
                }
            }

            Helpers.LogTrace($"video: delay={pictureDuration,-8:0.####} A-V={-clockDifference,-8:0.####}\n");

            return pictureDuration;
        }

        static double ComputePictureDuration(MediaContainer container, FrameHolder currentFrame, FrameHolder nextFrame)
        {
            if (currentFrame.Serial == nextFrame.Serial)
            {
                var pictureDuration = nextFrame.Time - currentFrame.Time;
                if (pictureDuration.IsNaN() || pictureDuration <= 0 || pictureDuration > container.MaxPictureDuration)
                    return currentFrame.Duration;
                else
                    return pictureDuration;
            }
            else
            {
                return 0.0;
            }
        }

        static void update_video_pts(MediaContainer container, double pts, int serial)
        {
            /* update current video pts */
            container.VideoClock.Set(pts, serial);
            container.ExternalClock.SyncToSlave(container.VideoClock);
        }
    }
}
