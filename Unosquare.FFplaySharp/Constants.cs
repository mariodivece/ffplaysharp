namespace Unosquare.FFplaySharp;

public static class Constants
{
    internal const int WaitTimeout = 10;

    internal const ThreadPriority ReadingPriority = ThreadPriority.BelowNormal;

    internal const ThreadPriority DecodingPriority = ThreadPriority.BelowNormal;

    /// <summary>
    /// Port of MAX_QUEUE_SIZE.
    /// </summary>
    public const int MaxQueueSize = 15 * 1024 * 1024;

    /// <summary>
    /// Minimum packet count for a stream to be deemed to contain enough packets.
    /// </summary>
    public const int MinPacketCount = 25;

    /// <summary>
    /// Port of EXTERNAL_CLOCK_MIN_FRAMES.
    /// </summary>
    public const int ExternalClockMinFrames = 2;

    /// <summary>
    /// Port of EXTERNAL_CLOCK_MAX_FRAMES.
    /// </summary>
    public const int ExternalClockMaxFrames = 10;

    /// <summary>
    /// no AV correction is done if too big error.
    /// Port of AV_NOSYNC_THRESHOLD.
    /// </summary>
    public const double MediaNoSyncThreshold = 10.0;

    /// <summary>
    /// maximum audio speed change to get correct sync.
    /// Port of SAMPLE_CORRECTION_PERCENT_MAX.
    /// </summary>
    public const double SampleCorrectionPercentMax = 10;

    /// <summary>
    /// External clock speed adjustment constants for realtime sources based on buffer fullness.
    /// Port of EXTERNAL_CLOCK_SPEED_MIN.
    /// </summary>
    public const double ExternalClockSpeedMin = 0.900;

    /// <summary>
    /// External clock speed adjustment constants for realtime sources based on buffer fullness.
    /// Port of EXTERNAL_CLOCK_SPEED_MAX.
    /// </summary>
    public const double ExternalClockSpeedMax = 1.010;

    /// <summary>
    /// External clock speed adjustment constants for realtime sources based on buffer fullness.
    /// Port of EXTERNAL_CLOCK_SPEED_STEP.
    /// </summary>
    public const double ExternalClockSpeedStep = 0.001;

    /// <summary>
    /// We use about AUDIO_DIFF_AVG_NB A-V differences to make the average.
    /// Port of AUDIO_DIFF_AVG_NB.
    /// </summary>
    public const int AudioDiffAveragesCount = 20;

    /// <summary>
    /// The size must be big enough to compensate the hardware audio buffersize size.
    /// TODO: We assume that a decoded and resampled frame fits into this buffer.
    /// Port of SAMPLE_ARRAY_SIZE.
    /// </summary>
    public const int SampleArraySize = 8 * 65536;

    /// <summary>
    /// Port of sws_flags. Represents the rescaler interpolation flags.
    /// Bilinear is fine and is faster. Bicubic is higher quality.
    /// Point uses no interpolation.
    /// </summary>
    public const int RescalerInterpolation = ffmpeg.SWS_POINT;

    public const int VideoFrameQueueCapacity = 3;
    public const int SubtitleFrameQueueCapacity = 16;
    public const int AudioFrameQueueCapacity = 9;

    public static readonly AVRational AV_TIME_BASE_Q = ffmpeg.av_make_q(1, ffmpeg.AV_TIME_BASE);

    public const int SeekMethodUnknownFlags = ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK;


}
