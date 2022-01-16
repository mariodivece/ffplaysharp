namespace Unosquare.FFplaySharp.Rendering;

using SDL2;


public unsafe class SdlVideoRenderer : IVideoRenderer
{
    private static readonly Dictionary<AVPixelFormat, uint> SdlTextureMap = new()
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

    private int DroppedPictureCount;
    private int DefaultWidth = 640;
    private int DefaultHeight = 480;

    private IntPtr RenderingWindow;
    private IntPtr SdlRenderer;
    private SDL.SDL_RendererInfo SdlRendererInfo;

    public string WindowTitle { get; set; }

    private IntPtr SubtitleTexture;
    private IntPtr VideoTexture;

    private int screen_left = SDL.SDL_WINDOWPOS_CENTERED;
    private int screen_top = SDL.SDL_WINDOWPOS_CENTERED;
    private bool is_full_screen;

    /// <summary>
    /// Port of last_time_status
    /// </summary>
    private double LastStatusLogTime;

    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public bool ForceRefresh { get; set; }

    public MediaContainer Container => Presenter.Container;

    public IPresenter Presenter { get; private set; }

    public void Initialize(IPresenter presenter)
    {
        Presenter = presenter;

        // Initialize renderer values based on user options
        var o = Presenter.Container.Options;
        ScreenWidth = o.WindowWidth;
        ScreenHeight = o.WindowHeight;
        is_full_screen = o.IsFullScreen;
        WindowTitle = o.WindowTitle;
        screen_left = o.WindowLeft ?? screen_left;
        screen_top = o.WindowTop ?? screen_top;

        if (o.IsDisplayDisabled)
            return;

        var parent = Presenter as SdlPresenter;
        parent.SdlInitFlags = (uint)SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        if (o.IsWindowAlwaysOnTop)
            parent.SdlInitFlags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP;

        if (o.IsWindowBorderless)
            parent.SdlInitFlags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_BORDERLESS;
        else
            parent.SdlInitFlags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;

        RenderingWindow = SDL.SDL_CreateWindow(
            Constants.ProgramName, SDL.SDL_WINDOWPOS_UNDEFINED, SDL.SDL_WINDOWPOS_UNDEFINED, DefaultWidth, DefaultHeight, (SDL.SDL_WindowFlags)parent.SdlInitFlags);

        SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_SCALE_QUALITY, "best");

        if (!RenderingWindow.IsNull())
        {
            SdlRenderer = SDL.SDL_CreateRenderer(RenderingWindow, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);
            if (SdlRenderer.IsNull())
            {
                ($"Failed to initialize a hardware accelerated renderer: {SDL.SDL_GetError()}.").LogWarning();
                SdlRenderer = SDL.SDL_CreateRenderer(RenderingWindow, -1, 0);
            }

            if (!SdlRenderer.IsNull())
            {
                if (SDL.SDL_GetRendererInfo(SdlRenderer, out SdlRendererInfo) == 0)
                    ($"Initialized {Helpers.PtrToString(SdlRendererInfo.name)} renderer.").LogVerbose();
            }
        }

        if (RenderingWindow.IsNull() || SdlRenderer.IsNull() || SdlRendererInfo.num_texture_formats <= 0)
        {
            var errorMessage = $"Failed to create window or renderer: {SDL.SDL_GetError()}.";
            errorMessage.LogFatal();
            throw new FFmpegException(ffmpeg.AVERROR_EXIT, errorMessage);
        }
    }

    public IEnumerable<AVPixelFormat> RetrieveSupportedPixelFormats()
    {
        var outputPixelFormats = new List<AVPixelFormat>(SdlTextureMap.Count);
        for (var i = 0; i < SdlRendererInfo.num_texture_formats; i++)
        {
            foreach (var kvp in SdlTextureMap)
            {
                if (kvp.Value == SdlRendererInfo.texture_formats[i])
                    outputPixelFormats.Add(kvp.Key);
            }
        }

        return outputPixelFormats;
    }

    /// <summary>
    /// Port of realloc_texture.
    /// </summary>
    /// <param name="texture"></param>
    /// <param name="sdlFormat"></param>
    /// <param name="pixelWidth"></param>
    /// <param name="pixelHeight"></param>
    /// <param name="blendMode"></param>
    /// <param name="zeroBytes"></param>
    /// <returns></returns>
    private int ReallocateTexture(
        ref IntPtr texture, uint sdlFormat, int pixelWidth, int pixelHeight, SDL.SDL_BlendMode blendMode, bool zeroBytes)
    {
        if (texture.IsNull() || SDL.SDL_QueryTexture(texture, out var format, out var _, out var w, out var h) < 0 || pixelWidth != w || pixelHeight != h || sdlFormat != format)
        {
            if (!texture.IsNull())
                SDL.SDL_DestroyTexture(texture);

            texture = SDL.SDL_CreateTexture(SdlRenderer, sdlFormat, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, pixelWidth, pixelHeight);
            if (texture.IsNull())
                return -1;

            if (SDL.SDL_SetTextureBlendMode(texture, blendMode) < 0)
                return -1;

            if (zeroBytes)
            {
                SDL.SDL_Rect rect = new() { w = pixelWidth, h = pixelHeight, x = 0, y = 0 };
                rect.w = pixelWidth;
                rect.h = pixelHeight;
                if (SDL.SDL_LockTexture(texture, ref rect, out var pixels, out var pitch) < 0)
                    return -1;

                var ptr = (byte*)pixels;
                for (var i = 0; i < pitch * pixelHeight; i++)
                {
                    ptr[i] = default;
                }

                SDL.SDL_UnlockTexture(texture);
            }

            ($"Created {pixelWidth}x{pixelHeight} texture with {SDL.SDL_GetPixelFormatName(sdlFormat)}.").LogVerbose();
        }
        return 0;
    }

    /// <summary>
    /// Port of video_image_display.
    /// </summary>
    /// <param name="container"></param>
    private void ComposePicture()
    {
        FrameHolder subtitle = null;
        var video = Container.Video.Frames.PeekPrevious();

        if (Container.HasSubtitles && Container.Subtitle.Frames.PendingCount > 0)
        {
            subtitle = Container.Subtitle.Frames.PeekCurrent();

            if (video.Time >= subtitle.StartDisplayTime)
            {
                if (!subtitle.IsUploaded)
                {
                    if (subtitle.Width <= 0 || subtitle.Height <= 0)
                        subtitle.UpdateSubtitleArea(video.Width, video.Height);

                    if (ReallocateTexture(ref SubtitleTexture, SDL.SDL_PIXELFORMAT_ARGB8888, subtitle.Width, subtitle.Height, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND, true) < 0)
                        return;

                    for (var i = 0; i < subtitle.Subtitle.Rects.Count; i++)
                    {
                        var targetRect = CreateRect(subtitle.Subtitle.Rects[i], subtitle);

                        Container.Subtitle.ConvertContext.Reallocate(
                            targetRect.w, targetRect.h, AVPixelFormat.AV_PIX_FMT_PAL8,
                            targetRect.w, targetRect.h, AVPixelFormat.AV_PIX_FMT_BGRA, 0);

                        if (Container.Subtitle.ConvertContext == null)
                        {
                            ("Cannot initialize the conversion context.").LogFatal();
                            return;
                        }

                        if (SDL.SDL_LockTexture(SubtitleTexture, ref targetRect, out var pixels, out var pitch) == 0)
                        {
                            Container.Subtitle.ConvertContext.Convert(
                                subtitle.Subtitle.Rects[i].Data,
                                subtitle.Subtitle.Rects[i].LineSize,
                                subtitle.Subtitle.Rects[i].H,
                                pixels,
                                pitch);

                            SDL.SDL_UnlockTexture(SubtitleTexture);
                        }
                    }

                    subtitle.MarkUploaded();
                }
            }
            else
            {
                subtitle = null;
            }

        }

        var rect = CalculateDisplayRect(
            Container.xleft, Container.ytop, Container.width, Container.height, video.Width, video.Height, video.Frame.SampleAspectRatio);

        if (!video.IsUploaded)
        {
            if (WriteTexture(ref VideoTexture, video, Container.Video.ConvertContext) < 0)
                return;

            video.MarkUploaded();
        }

        var point = new SDL.SDL_Point();
        _ = SDL.SDL_RenderCopyEx(SdlRenderer, VideoTexture, ref rect, ref rect, 0, ref point, video.IsPictureVerticalFlipped ? SDL.SDL_RendererFlip.SDL_FLIP_VERTICAL : SDL.SDL_RendererFlip.SDL_FLIP_NONE);

        if (subtitle != null)
        {
            var xratio = (double)rect.w / subtitle.Width;
            var yratio = (double)rect.h / subtitle.Height;

            for (var i = 0; i < subtitle.Subtitle.Rects.Count; i++)
            {
                var sourceRect = subtitle.Subtitle.Rects[i];

                SDL.SDL_Rect sdlSourceRect = new()
                {
                    x = sourceRect.X,
                    y = sourceRect.Y,
                    w = sourceRect.W,
                    h = sourceRect.H,
                };

                SDL.SDL_Rect target = new()
                {
                    x = (int)(rect.x + sdlSourceRect.x * xratio),
                    y = (int)(rect.y + sdlSourceRect.y * yratio),
                    w = (int)(sdlSourceRect.w * xratio),
                    h = (int)(sdlSourceRect.h * yratio)
                };

                _ = SDL.SDL_RenderCopy(SdlRenderer, SubtitleTexture, ref sdlSourceRect, ref target);
            }
        }
    }

    public void Close()
    {
        if (!SdlRenderer.IsNull())
            SDL.SDL_DestroyRenderer(SdlRenderer);

        if (!RenderingWindow.IsNull())
            SDL.SDL_DestroyWindow(RenderingWindow);

        if (!VideoTexture.IsNull())
            SDL.SDL_DestroyTexture(VideoTexture);

        if (!SubtitleTexture.IsNull())
            SDL.SDL_DestroyTexture(SubtitleTexture);
    }

    private void Render()
    {
        if (Container.width <= 0)
            OpenVideo();

        _ = SDL.SDL_SetRenderDrawColor(SdlRenderer, 0, 0, 0, 255);
        _ = SDL.SDL_RenderClear(SdlRenderer);

        if (Container.HasVideo)
            ComposePicture();

        SDL.SDL_RenderPresent(SdlRenderer);
    }

    private static SDL.SDL_Rect CreateRect(FFSubtitleRect rect, FrameHolder targetFrame)
    {
        var result = CreateRect(rect);
        result.x = result.x.Clamp(0, targetFrame.Width);
        result.y = result.y.Clamp(0, targetFrame.Height);
        result.w = result.w.Clamp(0, targetFrame.Width - result.x);
        result.h = result.h.Clamp(0, targetFrame.Height - result.y);

        return result;
    }

    private static SDL.SDL_Rect CreateRect(FFSubtitleRect rect)
    {
        return new SDL.SDL_Rect
        {
            x = rect.X,
            y = rect.Y,
            w = rect.W,
            h = rect.H
        };
    }

    private static SDL.SDL_Rect CreateRect(FrameHolder frame)
    {
        return new SDL.SDL_Rect { w = frame.Width, h = frame.Height, x = 0, y = 0 };
    }

    /// <summary>
    /// Port of upload_texture.
    /// </summary>
    /// <param name="texture"></param>
    /// <param name="video"></param>
    /// <param name="convertContext"></param>
    /// <returns></returns>
    private int WriteTexture(ref IntPtr texture, FrameHolder video, RescalerContext convertContext)
    {
        var resultCode = 0;
        var frame = video.Frame;
        (var sdlPixelFormat, var sdlBlendMode) = TranslateToSdlFormat(frame.PixelFormat);
        var textureFormat = sdlPixelFormat == SDL.SDL_PIXELFORMAT_UNKNOWN ? SDL.SDL_PIXELFORMAT_ARGB8888 : sdlPixelFormat;

        if (ReallocateTexture(ref texture, textureFormat, video.Width, video.Height, sdlBlendMode, false) < 0)
            return -1;

        var textureRect = CreateRect(video);

        if (sdlPixelFormat == SDL.SDL_PIXELFORMAT_UNKNOWN)
        {
            // This should only happen if we are not using avfilter...
            convertContext.Reallocate(
                video.Width, video.Height, frame.PixelFormat, video.Width, video.Height, AVPixelFormat.AV_PIX_FMT_BGRA);

            if (convertContext != null)
            {
                if (SDL.SDL_LockTexture(texture, ref textureRect, out var textureAddress, out var texturePitch) == 0)
                {
                    convertContext.Convert(frame.Data, frame.LineSize, video.Height, textureAddress, texturePitch);
                    SDL.SDL_UnlockTexture(texture);
                }
            }
            else
            {
                ("Cannot initialize the conversion context.").LogFatal();
                resultCode = -1;
            }
        }
        else if (sdlPixelFormat == SDL.SDL_PIXELFORMAT_IYUV)
        {
            if (frame.LineSize[0] > 0 && frame.LineSize[1] > 0 && frame.LineSize[2] > 0)
            {
                resultCode = SDL.SDL_UpdateYUVTexture(texture, ref textureRect,
                    (IntPtr)frame.Data[0], frame.LineSize[0],
                    (IntPtr)frame.Data[1], frame.LineSize[1],
                    (IntPtr)frame.Data[2], frame.LineSize[2]);
            }
            else if (frame.LineSize[0] < 0 && frame.LineSize[1] < 0 && frame.LineSize[2] < 0)
            {
                resultCode = SDL.SDL_UpdateYUVTexture(texture, ref textureRect,
                    (IntPtr)frame.Data[0] + frame.LineSize[0] * (video.Height - 1), -frame.LineSize[0],
                    (IntPtr)frame.Data[1] + frame.LineSize[1] * (Helpers.AV_CEIL_RSHIFT(video.Height, 1) - 1), -frame.LineSize[1],
                    (IntPtr)frame.Data[2] + frame.LineSize[2] * (Helpers.AV_CEIL_RSHIFT(video.Height, 1) - 1), -frame.LineSize[2]);
            }
            else
            {
                ("Mixed negative and positive linesizes are not supported.").LogError();
                return -1;
            }
        }
        else
        {
            resultCode = (frame.LineSize[0] < 0)
                ? SDL.SDL_UpdateTexture(texture, ref textureRect, (IntPtr)frame.Data[0] + frame.LineSize[0] * (video.Height - 1), -frame.LineSize[0])
                : SDL.SDL_UpdateTexture(texture, ref textureRect, (IntPtr)frame.Data[0], frame.LineSize[0]);

        }

        return resultCode;
    }

    /// <summary>
    /// Port of video_open
    /// </summary>
    /// <returns></returns>
    public int OpenVideo()
    {
        var w = ScreenWidth != 0 ? ScreenWidth : DefaultWidth;
        var h = ScreenHeight != 0 ? ScreenHeight : DefaultHeight;

        if (string.IsNullOrWhiteSpace(WindowTitle))
            WindowTitle = Container.Options.InputFileName;
        SDL.SDL_SetWindowTitle(RenderingWindow, WindowTitle);

        SDL.SDL_SetWindowSize(RenderingWindow, w, h);
        SDL.SDL_SetWindowPosition(RenderingWindow, screen_left, screen_top);
        if (is_full_screen)
            _ = SDL.SDL_SetWindowFullscreen(RenderingWindow, (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP);

        SDL.SDL_ShowWindow(RenderingWindow);

        Container.width = w;
        Container.height = h;

        return 0;
    }

    /* called to display each frame */
    public void Present(ref double remainingTime)
    {
        if (!Container.IsPaused && Container.MasterSyncMode == ClockSource.External && Container.IsRealTime)
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
                var previousPicture = Container.Video.Frames.PeekPrevious();
                var currentPicture = Container.Video.Frames.PeekCurrent();

                if (currentPicture.GroupIndex != Container.Video.Packets.GroupIndex)
                {
                    Container.Video.Frames.Dequeue();
                    goto retry;
                }

                if (previousPicture.GroupIndex != currentPicture.GroupIndex)
                    Container.PictureDisplayTimer = Clock.SystemTime;

                if (Container.IsPaused)
                    goto display;

                // compute nominal last_duration
                var pictureDuration = ComputePictureDuration(Container, previousPicture, currentPicture);
                var pictureDisplayDuration = ComputePictureDisplayDuration(pictureDuration, Container);

                var currentTime = Clock.SystemTime;
                if (currentTime < Container.PictureDisplayTimer + pictureDisplayDuration)
                {
                    remainingTime = Math.Min(Container.PictureDisplayTimer + pictureDisplayDuration - currentTime, remainingTime);
                    goto display;
                }

                Container.PictureDisplayTimer += pictureDisplayDuration;
                if (pictureDisplayDuration > 0 && currentTime - Container.PictureDisplayTimer > Constants.MediaSyncThresholdMax)
                    Container.PictureDisplayTimer = currentTime;

                if (currentPicture.HasValidTime)
                    UpdateVideoPts(Container, currentPicture.Time, currentPicture.GroupIndex);

                if (Container.Video.Frames.PendingCount > 1)
                {
                    var nextPicture = Container.Video.Frames.PeekNext();
                    var duration = ComputePictureDuration(Container, currentPicture, nextPicture);
                    if (Container.IsInStepMode == false &&
                        (Container.Options.IsFrameDropEnabled > 0 ||
                        (Container.Options.IsFrameDropEnabled != 0 && Container.MasterSyncMode != ClockSource.Video)) &&
                        currentTime > Container.PictureDisplayTimer + duration)
                    {
                        DroppedPictureCount++;
                        Container.Video.Frames.Dequeue();
                        goto retry;
                    }
                }

                if (Container.HasSubtitles)
                {
                    while (Container.Subtitle.Frames.PendingCount > 0)
                    {
                        var sp = Container.Subtitle.Frames.PeekCurrent();
                        var sp2 = Container.Subtitle.Frames.PendingCount > 1
                            ? Container.Subtitle.Frames.PeekNext()
                            : null;

                        if (sp.GroupIndex != Container.Subtitle.Packets.GroupIndex
                                || (Container.VideoClock.BaseTime > sp.EndDisplayTime)
                                || (sp2 != null && Container.VideoClock.BaseTime > sp2.StartDisplayTime))
                        {
                            if (sp.IsUploaded)
                            {
                                for (var i = 0; i < sp.Subtitle.Rects.Count; i++)
                                {
                                    var sdlRect = CreateRect(sp.Subtitle.Rects[i]);

                                    if (SDL.SDL_LockTexture(SubtitleTexture, ref sdlRect, out var pixels, out var pitch) == 0)
                                    {
                                        var ptr = (byte*)pixels;
                                        for (var j = 0; j < sdlRect.h; j++, ptr += pitch)
                                        {
                                            for (var b = 0; b < sdlRect.w << 2; b++)
                                            {
                                                ptr[b] = byte.MinValue;
                                            }
                                        }

                                        SDL.SDL_UnlockTexture(SubtitleTexture);
                                    }
                                }
                            }
                            Container.Subtitle.Frames.Dequeue();
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                Container.Video.Frames.Dequeue();
                ForceRefresh = true;

                if (Container.IsInStepMode && !Container.IsPaused)
                    Container.StreamTogglePause();
            }
        display:
            /* display picture */
            if (!Container.Options.IsDisplayDisabled && ForceRefresh && Container.ShowMode == ShowMode.Video && Container.Video.Frames.IsReadIndexShown)
                Render();
        }

        ForceRefresh = false;
        if (Container.Options.ShowStatus != 0)
        {
            var currentTime = Clock.SystemTime;
            if (LastStatusLogTime == 0 || (currentTime - LastStatusLogTime) >= 0.03)
            {
                var audioQueueSize = Container.HasAudio ? Container.Audio.Packets.ByteSize : 0;
                var videoQueueSize = Container.HasVideo ? Container.Video.Packets.ByteSize : 0;
                var subtitleQueueSize = Container.HasSubtitles ? Container.Subtitle.Packets.ByteSize : 0;
                var audioVideoDelay = Container.ComponentSyncDelay;
                var ci = CultureInfo.InvariantCulture;

                var buf = new StringBuilder()
                    .Append(ci, $"{Container.MasterTime,-8:0.####} | ")
                    .Append((Container.HasAudio && Container.HasVideo) ? "A-V" : (Container.HasVideo ? "M-V" : (Container.HasAudio ? "M-A" : "   ")))
                    .Append(ci, $":{audioVideoDelay,9:0.####} | ")
                    .Append(ci, $"fd={(Container.Video.DroppedFrameCount + DroppedPictureCount)} | ")
                    .Append(ci, $"aq={(audioQueueSize / 1024)}KB | ")
                    .Append(ci, $"vq={(videoQueueSize / 1024)}KB | ")
                    .Append(ci, $"sq={subtitleQueueSize}B | ")
                    .Append(ci, $" f={(Container.HasVideo ? Container.Video.CodecContext.FaultyDtsCount : 0)} / ")
                    .Append(ci, $"{(Container.HasVideo ? Container.Video.CodecContext.FaultyPtsCount : 0)}");

                var paddingLength = 90 - buf.Length;
                if (paddingLength > 0)
                    buf.Append(' ', paddingLength);

                if (Container.Options.ShowStatus == ThreeState.On && FFLog.Level < ffmpeg.AV_LOG_INFO)
                    Console.Write($"{buf}\r");
                else
                    ($"{buf}\r").LogInfo(false);

                LastStatusLogTime = currentTime;
            }
        }
    }

    public void ToggleFullScreen()
    {
        is_full_screen = !is_full_screen;
        _ = SDL.SDL_SetWindowFullscreen(RenderingWindow, (uint)(is_full_screen ? SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP : 0));
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

        if (SdlTextureMap.ContainsKey(pixelFormat))
            sdlPixelFormat = SdlTextureMap[pixelFormat];

        return (sdlPixelFormat, sdlBlendMode);
    }

    /// <summary>
    /// Port of set_default_window_size
    /// </summary>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <param name="sar"></param>
    public void SetDefaultWindowSize(int width, int height, AVRational sar)
    {
        var maxWidth = ScreenWidth != 0 ? ScreenWidth : int.MaxValue;
        var maxHeight = ScreenHeight != 0 ? ScreenHeight : int.MaxValue;
        if (maxWidth == int.MaxValue && maxHeight == int.MaxValue)
            maxHeight = height;

        var rect = CalculateDisplayRect(0, 0, maxWidth, maxHeight, width, height, sar);
        DefaultWidth = rect.w;
        DefaultHeight = rect.h;
    }

    static double ComputePictureDisplayDuration(double pictureDuration, MediaContainer container)
    {
        var clockDifference = 0d;

        /* update delay to follow master synchronisation source */
        if (container.MasterSyncMode != ClockSource.Video)
        {
            /* if video is slave, we try to correct big delays by
               duplicating or deleting a frame */
            clockDifference = container.VideoClock.Value - container.MasterTime;

            /* skip or repeat frame. We take into account the
               delay to compute the threshold. I still don't know
               if it is the best guess */
            var syncThreshold = Math.Max(Constants.MediaSyncThresholdMin, Math.Min(Constants.MediaSyncThresholdMax, pictureDuration));
            if (!clockDifference.IsNaN() && Math.Abs(clockDifference) < container.MaxPictureDuration)
            {
                if (clockDifference <= -syncThreshold)
                    pictureDuration = Math.Max(0, pictureDuration + clockDifference);
                else if (clockDifference >= syncThreshold && pictureDuration > Constants.MediaSyncFrameDupThreshold)
                    pictureDuration += clockDifference;
                else if (clockDifference >= syncThreshold)
                    pictureDuration = 2 * pictureDuration;
            }
        }

        ($"video: delay={pictureDuration,-8:0.####} A-V={-clockDifference,-8:0.####}.").LogTrace();

        return pictureDuration;
    }

    static double ComputePictureDuration(MediaContainer container, FrameHolder currentFrame, FrameHolder nextFrame)
    {
        if (currentFrame.GroupIndex != nextFrame.GroupIndex)
            return 0.0;

        var pictureDuration = nextFrame.Time - currentFrame.Time;
        if (pictureDuration.IsNaN() || pictureDuration <= 0 || pictureDuration > container.MaxPictureDuration)
            return currentFrame.Duration;
        else
            return pictureDuration;
    }

    /// <summary>
    /// Port of update_video_pts
    /// </summary>
    /// <param name="container"></param>
    /// <param name="pts"></param>
    /// <param name="groupIndex"></param>
    static void UpdateVideoPts(MediaContainer container, double pts, int groupIndex)
    {
        /* update current video pts */
        container.VideoClock.Set(pts, groupIndex);
        container.ExternalClock.SyncToSlave(container.VideoClock);
    }
}
