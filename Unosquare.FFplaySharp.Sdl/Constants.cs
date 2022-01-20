namespace Unosquare.FFplaySharp.Sdl;

internal static class Constants
{
    public const string ProgramName = "ffplay";
    public const int ProgramBirthYear = 2003;

    public const int FF_QUIT_EVENT = (int)SDL.SDL_EventType.SDL_USEREVENT + 2;

    /// <summary>
    /// Minimum SDL audio buffer size, in samples.
    /// Port of SDL_AUDIO_MIN_BUFFER_SIZE.
    /// </summary>
    public const int SdlAudioMinBufferSize = 512;

    /// <summary>
    /// Calculate actual buffer size keeping in mind not cause too frequent audio callbacks.
    /// Port of SDL_AUDIO_MAX_CALLBACKS_PER_SEC.
    /// </summary>
    public const int SdlAudioMaxCallbacksPerSec = 30;

    /// <summary>
    /// Step size for volume control in dB.
    /// Port of SDL_VOLUME_STEP.
    /// </summary>
    public const double SdlVolumeStep = 0.75;

    /// <summary>
    /// Port of USE_ONEPASS_SUBTITLE_RENDER.
    /// </summary>
    public const bool UseOnePassSubtitleRender = true;

    /// <summary>
    /// Port of CURSOR_HIDE_DELAY.
    /// </summary>
    public const double CursorHideDelay = 1d;

    /// <summary>
    /// Polls for possible required screen refresh at least this often, should be less than 1/fps.
    /// Port of REFRESH_RATE.
    /// </summary>
    public const double RefreshRate = 0.01;

    /// <summary>
    /// No AV sync correction is done if below the minimum AV sync threshold.
    /// Port of AV_SYNC_THRESHOLD_MIN.
    /// </summary>
    public const double MediaSyncThresholdMin = 0.04;

    /// <summary>
    /// AV sync correction is done if above the maximum AV sync threshold.
    /// Port of AV_SYNC_THRESHOLD_MAX.
    /// </summary>
    public const double MediaSyncThresholdMax = 0.1;

    /// <summary>
    /// If a frame duration is longer than this, it will not be duplicated to compensate AV sync.
    /// Port of AV_SYNC_FRAMEDUP_THRESHOLD.
    /// </summary>
    public const double MediaSyncFrameDupThreshold = 0.1;

    public static readonly AVPixelFormat AV_PIX_FMT_RGB32 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_ARGB, AVPixelFormat.AV_PIX_FMT_BGRA);
    public static readonly AVPixelFormat AV_PIX_FMT_RGB32_1 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_RGBA, AVPixelFormat.AV_PIX_FMT_ABGR);
    public static readonly AVPixelFormat AV_PIX_FMT_BGR32 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_ABGR, AVPixelFormat.AV_PIX_FMT_RGBA);
    public static readonly AVPixelFormat AV_PIX_FMT_BGR32_1 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_BGRA, AVPixelFormat.AV_PIX_FMT_ARGB);
    public static readonly AVPixelFormat AV_PIX_FMT_0RGB32 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_0RGB, AVPixelFormat.AV_PIX_FMT_BGR0);
    public static readonly AVPixelFormat AV_PIX_FMT_0BGR32 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_0BGR, AVPixelFormat.AV_PIX_FMT_RGB0);
    public static readonly AVPixelFormat AV_PIX_FMT_RGB444 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_RGB444BE, AVPixelFormat.AV_PIX_FMT_RGB444LE);
    public static readonly AVPixelFormat AV_PIX_FMT_RGB555 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_RGB555BE, AVPixelFormat.AV_PIX_FMT_RGB555LE);
    public static readonly AVPixelFormat AV_PIX_FMT_BGR555 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_BGR555BE, AVPixelFormat.AV_PIX_FMT_BGR555LE);
    public static readonly AVPixelFormat AV_PIX_FMT_RGB565 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_RGB565BE, AVPixelFormat.AV_PIX_FMT_RGB565LE);
    public static readonly AVPixelFormat AV_PIX_FMT_BGR565 = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_BGR565BE, AVPixelFormat.AV_PIX_FMT_BGR565LE);

    public static readonly AVPixelFormat AV_PIX_FMT_0BGRLE = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_RGB0, AVPixelFormat.AV_PIX_FMT_0BGR);
    public static readonly AVPixelFormat AV_PIX_FMT_0RGBLE = AV_PIX_FMT_NE(AVPixelFormat.AV_PIX_FMT_BGR0, AVPixelFormat.AV_PIX_FMT_0RGB);

    public static AVPixelFormat AV_PIX_FMT_NE(AVPixelFormat bigEndian, AVPixelFormat littleEndian) =>
        BitConverter.IsLittleEndian ? littleEndian : bigEndian;
}

