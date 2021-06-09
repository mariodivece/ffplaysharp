namespace FFmpeg
{
    using System;

    public class FFmpegException : Exception
    {
        public FFmpegException(int errorCode)
        {
            ErrorCode = errorCode;
        }

        public int ErrorCode { get; }
    }
}
