namespace Unosquare.FFplaySharp;

using SDL2;


public static class Constants
{
    public const int EventWaitTime = 1;

    public const string ProgramName = "ffplay";
    public const int ProgramBirthYear = 2003;

    /// <summary>
    /// Port of MAX_QUEUE_SIZE
    /// </summary>
    public const int MaxQueueSize = 15 * 1024 * 1024;
    public const int MinPacketCount = 25;

    /// <summary>
    /// Port of EXTERNAL_CLOCK_MIN_FRAMES
    /// </summary>
    public const int ExternalClockMinFrames = 2;

    /// <summary>
    /// Port of EXTERNAL_CLOCK_MAX_FRAMES
    /// </summary>
    public const int ExternalClockMaxFrames = 10;

    /// <summary>
    /// Minimum SDL audio buffer size, in samples.
    /// Port of SDL_AUDIO_MIN_BUFFER_SIZE
    /// </summary>
    public const int SdlAudioMinBufferSize = 512;

    /// <summary>
    /// Calculate actual buffer size keeping in mind not cause too frequent audio callbacks.
    /// Port of SDL_AUDIO_MAX_CALLBACKS_PER_SEC
    /// </summary>
    public const int SdlAudioMaxCallbacksPerSec = 30;

    /// <summary>
    /// Step size for volume control in dB
    /// Port of SDL_VOLUME_STEP
    /// </summary>
    public const double SdlVolumeStep = 0.75;

    /// <summary>
    /// No AV sync correction is done if below the minimum AV sync threshold.
    /// Port of AV_SYNC_THRESHOLD_MIN
    /// </summary>
    public const double MediaSyncThresholdMin = 0.04;

    /// <summary>
    /// AV sync correction is done if above the maximum AV sync threshold.
    /// Port of AV_SYNC_THRESHOLD_MAX
    /// </summary>
    public const double MediaSyncThresholdMax = 0.1;

    /// <summary>
    /// If a frame duration is longer than this, it will not be duplicated to compensate AV sync.
    /// Port of AV_SYNC_FRAMEDUP_THRESHOLD
    /// </summary>
    public const double MediaSyncFrameDupThreshold = 0.1;

    /// <summary>
    /// no AV correction is done if too big error.
    /// Port of AV_NOSYNC_THRESHOLD
    /// </summary>
    public const double MediaNoSyncThreshold = 10.0;

    /// <summary>
    /// maximum audio speed change to get correct sync.
    /// Port of SAMPLE_CORRECTION_PERCENT_MAX
    /// </summary>
    public const double SampleCorrectionPercentMax = 10;

    /// <summary>
    /// External clock speed adjustment constants for realtime sources based on buffer fullness.
    /// Port of EXTERNAL_CLOCK_SPEED_MIN
    /// </summary>
    public const double ExternalClockSpeedMin = 0.900;

    /// <summary>
    /// External clock speed adjustment constants for realtime sources based on buffer fullness.
    /// Port of EXTERNAL_CLOCK_SPEED_MAX
    /// </summary>
    public const double ExternalClockSpeedMax = 1.010;

    /// <summary>
    /// External clock speed adjustment constants for realtime sources based on buffer fullness.
    /// Port of EXTERNAL_CLOCK_SPEED_STEP
    /// </summary>
    public const double ExternalClockSpeedStep = 0.001;

    /// <summary>
    /// We use about AUDIO_DIFF_AVG_NB A-V differences to make the average.
    /// Port of AUDIO_DIFF_AVG_NB
    /// </summary>
    public const int AudioDiffAveragesCount = 20;

    /// <summary>
    /// Polls for possible required screen refresh at least this often, should be less than 1/fps.
    /// Port of REFRESH_RATE
    /// </summary>
    public const double RefreshRate = 0.01;

    /// <summary>
    /// The size must be big enough to compensate the hardware audio buffersize size.
    /// TODO: We assume that a decoded and resampled frame fits into this buffer.
    /// Port of SAMPLE_ARRAY_SIZE
    /// </summary>
    public const int SampleArraySize = 8 * 65536;

    /// <summary>
    /// Port of CURSOR_HIDE_DELAY
    /// </summary>
    public const double CursorHideDelay = 1d;

    /// <summary>
    /// Port of USE_ONEPASS_SUBTITLE_RENDER
    /// </summary>
    public const bool UseOnePassSubtitleRender = true;

    /// <summary>
    /// Port of sws_flags. Represents the rescaler interpolation flags.
    /// Bilinear is fine and is faster. Bicubic is higher quality.
    /// </summary>
    public const int RescalerInterpolation = ffmpeg.SWS_BICUBIC;

    public const int VideoFrameQueueCapacity = 3;
    public const int SubtitleFrameQueueCapacity = 16;
    public const int AudioFrameQueueCapacity = 9;

    public static readonly AVRational AV_TIME_BASE_Q = ffmpeg.av_make_q(1, ffmpeg.AV_TIME_BASE);

    public const int FF_QUIT_EVENT = (int)SDL.SDL_EventType.SDL_USEREVENT + 2;

    public const int SeekMethodUnknownFlags = ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK;

#pragma warning disable CA1707 // Identifiers should not contain underscores
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
#pragma warning restore CA1707 // Identifiers should not contain underscores
}
