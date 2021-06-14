namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;

    public static class Constants
    {
        public const string program_name = "ffplay";
        public const int program_birth_year = 2003;

        public const int MAX_QUEUE_SIZE = 15 * 1024 * 1024;
        public const int MinPacketCount = 25;
        public const int EXTERNAL_CLOCK_MIN_FRAMES = 2;
        public const int EXTERNAL_CLOCK_MAX_FRAMES = 10;

        /* Minimum SDL audio buffer size, in samples. */
        public const int SDL_AUDIO_MIN_BUFFER_SIZE = 512;
        /* Calculate actual buffer size keeping in mind not cause too frequent audio callbacks */
        public const int SDL_AUDIO_MAX_CALLBACKS_PER_SEC = 30;

        /* Step size for volume control in dB */
        public const double SDL_VOLUME_STEP = 0.75;

        /* no AV sync correction is done if below the minimum AV sync threshold */
        public const double AV_SYNC_THRESHOLD_MIN = 0.04;
        /* AV sync correction is done if above the maximum AV sync threshold */
        public const double AV_SYNC_THRESHOLD_MAX = 0.1;
        /* If a frame duration is longer than this, it will not be duplicated to compensate AV sync */
        public const double AV_SYNC_FRAMEDUP_THRESHOLD = 0.1;
        /* no AV correction is done if too big error */
        public const double AV_NOSYNC_THRESHOLD = 10.0;

        /* maximum audio speed change to get correct sync */
        public const double SAMPLE_CORRECTION_PERCENT_MAX = 10;

        /* external clock speed adjustment constants for realtime sources based on buffer fullness */
        public const double EXTERNAL_CLOCK_SPEED_MIN = 0.900;
        public const double EXTERNAL_CLOCK_SPEED_MAX = 1.010;
        public const double EXTERNAL_CLOCK_SPEED_STEP = 0.001;

        /* we use about AUDIO_DIFF_AVG_NB A-V differences to make the average */
        public const int AUDIO_DIFF_AVG_NB = 20;

        /* polls for possible required screen refresh at least this often, should be less than 1/fps */
        public const double REFRESH_RATE = 0.01;

        /* NOTE: the size must be big enough to compensate the hardware audio buffersize size */
        /* TODO: We assume that a decoded and resampled frame fits into this buffer */
        public const int SAMPLE_ARRAY_SIZE = (8 * 65536);

        public const double CURSOR_HIDE_DELAY = 1d;

        public const int USE_ONEPASS_SUBTITLE_RENDER = 1;

        /// <summary>
        /// Port of sws_flags. Represents the 
        /// </summary>
        public const int RescalerInterpolation = ffmpeg.SWS_BICUBIC;

        public const int VideoFrameQueueCapacity = 3;
        public const int SubtitleFrameQueueCapacity = 16;
        public const int AudioFrameQueueCapacity = 9;

        public static readonly AVRational AV_TIME_BASE_Q = ffmpeg.av_make_q(1, ffmpeg.AV_TIME_BASE);

        public const int FF_QUIT_EVENT = (int)SDL.SDL_EventType.SDL_USEREVENT + 2;

        public const int SeekMethodUnknownFlags = ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK;

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

        public static AVPixelFormat AV_PIX_FMT_NE(AVPixelFormat be, AVPixelFormat le)
            => BitConverter.IsLittleEndian ? le : be;
    }
}
