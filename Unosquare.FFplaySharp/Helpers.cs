namespace Unosquare.FFplaySharp;

public static class Helpers
{
    private const string FFmpegDirectory = @"c:\ffmpeg\x64";

    public static void SetFFmpegRootPath(string path = FFmpegDirectory) => ffmpeg.RootPath = path;

    public static bool HasFlag(this int flagsVariable, int flagValue) => (flagsVariable & flagValue) != 0;

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

    public static int Clamp(this int number, int min, int max) => number < min ? min : number > max ? max : number;

    public static double ToFactor(this AVRational r) => ffmpeg.av_q2d(r);

    public static double ToDouble(this int m) => Convert.ToDouble(m);

    public static bool IsAudio(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_AUDIO;

    public static bool IsVideo(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_VIDEO;

    public static bool IsSubtitle(this AVMediaType t) => t == AVMediaType.AVMEDIA_TYPE_SUBTITLE;

    public static string ToName(this AVMediaType t) => ffmpeg.av_get_media_type_string(t);

    public static unsafe string PtrToString(byte* ptr) => PtrToString((IntPtr)ptr);

    public static unsafe string PtrToString(IntPtr address)
    {
        if (address.IsNull())
            return null;

        var source = (byte*)address.ToPointer();
        var length = 0;
        while (source[length] != 0)
            ++length;

        if (length == 0)
            return string.Empty;

        var target = stackalloc byte[length];
        Buffer.MemoryCopy(source, target, length, length);
        return Encoding.UTF8.GetString(target, length);
    }

    public static bool IsValidPts(this long pts) => pts != ffmpeg.AV_NOPTS_VALUE;

    public static bool IsAuto(this int x) => x < 0;

    public static bool IsAuto(this ThreeState x) => ((int)x).IsAuto();

    public static ThreeState ToThreeState(this int x) => x < 0 ? ThreeState.Auto : x > 0 ? ThreeState.On : ThreeState.Off;

    public static bool IsFalse(this int x) => x == 0;

    public static bool IsNaN(this double x) => double.IsNaN(x);

    public static bool IsNull(this IntPtr ptr) => ptr == IntPtr.Zero;
}
