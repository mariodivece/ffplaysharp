namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class MediaClass : UnmanagedReference<AVClass>
    {
        public static readonly MediaClass Codec = new(ffmpeg.avcodec_get_class());
        public static readonly MediaClass Format = new(ffmpeg.avformat_get_class());
        public static readonly MediaClass Scaler = new(ffmpeg.sws_get_class());
        public static readonly MediaClass Resampler = new(ffmpeg.swr_get_class());

        private MediaClass(AVClass* pointer)
            : base(pointer)
        {
            // placeholder
        }

        /// <summary>
        /// Port of opt_find. Finds an option in a given ffmpeg class.
        /// </summary>
        /// <param name="classObject"></param>
        /// <param name="optionName"></param>
        /// <param name="searchFlags"></param>
        /// <returns></returns>
        public AVOption* FindOption(string optionName, int optionFlags, int searchFlags)
        {
            if (Pointer == null)
                return null;

            var option = ffmpeg.av_opt_find(Pointer, optionName, null, optionFlags, searchFlags);
            if (option != null && option->flags == 0)
                return null;

            return option;
        }

        public AVOption* FindOption(string optionName, int searchFlags) =>
            FindOption(optionName, 0, searchFlags);

        public AVOption* FindOption(string optionName) =>
            FindOption(optionName, 0, ffmpeg.AV_OPT_SEARCH_FAKE_OBJ);

        public static MediaClass FromPrivateClass(AVClass* pointer) =>
            pointer == null ? null : new MediaClass(pointer);
    }
}
