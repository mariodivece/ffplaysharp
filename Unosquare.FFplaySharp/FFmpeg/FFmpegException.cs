namespace FFmpeg;

public class FFmpegException : Exception
{
    public FFmpegException(int errorCode)
        : base(DescribeError(errorCode))
    {
        ErrorCode = errorCode;
    }

    public FFmpegException(int errorCode, string userMessage, Exception innerException = null)
        : base($"{userMessage}\r\nFFmpeg Error: {DescribeError(errorCode)}", innerException)
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }

    /// <summary>
    /// Port of print_error. Gets a string representation of an FFmpeg error code.
    /// </summary>
    /// <param name="errorCode">The FFmpeg error code.</param>
    /// <returns>The text representation of the error code.</returns>
    public static unsafe string DescribeError(int errorCode)
    {
        const int BufferSize = 2048;
        var buffer = stackalloc byte[BufferSize];
        var foundError = ffmpeg.av_strerror(errorCode, buffer, BufferSize) == 0;
        var message = foundError ? Helpers.PtrToString(buffer) : $"Error with code ({errorCode})";
        return message;
    }
}
