using Unosquare.FFplaySharp.Interop;

namespace Unosquare.FFplaySharp;

public static class Helpers
{
    private const string FFmpegDirectory = @"c:\ffmpeg\x64";

    public static void SetFFmpegRootPath(string path = FFmpegDirectory) => ffmpeg.RootPath = path;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasFlag(this int flagsVariable, int flagValue) => (flagsVariable & flagValue) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AV_CEIL_RSHIFT(int a, int b) => ((a) + (1 << (b)) - 1) >> (b);

    /// <summary>
    /// Parses a hexagesimal (HOURS:MM:SS.MILLISECONDS) or simple second
    /// and decimal string representing time and returns total microseconds.
    /// </summary>
    /// <param name="timeString">The time represented as a string.</param>
    /// <returns>Total microseconds.</returns>
    public static long ParseTime(this string timeString)
    {
        var semicolonSeparator = new char[] { ':' };
        // HOURS:MM:SS.MILLISECONDS
        var timeStr = timeString.Trim();
        var segments = timeStr.Split(semicolonSeparator).Select(c => c.Trim()).Reverse().ToArray();
        var (hours, minutes, seconds) = (0M, 0M, 0M);
        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var value = decimal.TryParse(segments[segmentIndex], out var parsedValue) ? parsedValue : 0;
            switch (segmentIndex)
            {
                case 0:
                    seconds = value;
                    break;
                case 1:
                    minutes = value;
                    break;
                case 2:
                    hours = value;
                    break;
                default:
                    break;
            }
        }

        var isNegative = seconds < 0 || hours < 0;
        var secondsSum = Math.Abs(hours * 60 * 60)
            + Math.Abs(minutes * 60)
            + Math.Abs(seconds);

        var totalSeconds = secondsSum * (isNegative ? -1M : 1M);
        return Convert.ToInt64(totalSeconds) * ffmpeg.AV_TIME_BASE;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(this int number, int min, int max) => number < min ? min : number > max ? max : number;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Clamp(this long number, long min, long max) => number < min ? min : number > max ? max : number;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(this double number, double min, double max) => number < min ? min : number > max ? max : number;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToFactor(this AVRational r) => ffmpeg.av_q2d(r);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDouble(this int m) => Convert.ToDouble(m);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAudio(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_AUDIO;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVideo(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_VIDEO;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSubtitle(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToName(this AVMediaType t) => ffmpeg.av_get_media_type_string(t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe string? PtrToString(byte* target) => target is null
        ? default : PtrToString((nint)target);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe string? PtrToString(nint address) => address == nint.Zero ?
        default : (Marshal.PtrToStringUTF8(address) ?? string.Empty);

    /// <summary>
    /// Gets a value indicating whether a timestamp does not have a value of
    /// <see cref="ffmpeg.AV_NOPTS_VALUE"/>.
    /// </summary>
    /// <param name="value">The timestamp value.</param>
    /// <returns>The result.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValidTimestamp(this long pts) => pts != ffmpeg.AV_NOPTS_VALUE;

    /// <summary>
    /// Converts a <see cref="ffmpeg.AV_NOPTS_VALUE"/> to a null value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The nullable value.</returns>
    public static long? ToNullable(this long value) => value == ffmpeg.AV_NOPTS_VALUE ? null : value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAuto(this int x) => x < 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAuto(this ThreeState x) => ((int)x).IsAuto();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ThreeState ToThreeState(this int x) => x < 0 ? ThreeState.Auto : x > 0 ? ThreeState.On : ThreeState.Off;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFalse(this int x) => x == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNullPointer(this nint address) => address == nint.Zero;
}
