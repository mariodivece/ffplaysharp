namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFormatContext : CountedReference<AVFormatContext>
    {
        public FFFormatContext([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.avformat_alloc_context());
            Streams = new(this);
            Chapters = new(this);
        }

        public AVIOInterruptCB_callback_func InterruptCallback
        {
            get => Target->interrupt_callback.callback;
            set => Target->interrupt_callback.callback = value;
        }

        public StreamSet Streams { get; }

        public ChapterSet Chapters { get; }

        public FFInputFormat? InputFormat => Target->iformat is not null
            ? new(Target->iformat)
            : default;

        public FFIOContext? IO => Target->pb is not null
            ? new(Target->pb)
            : default;

        public IReadOnlyDictionary<string, string> Metadata =>
            FFDictionary.Extract(Target->metadata);

        public int Flags
        {
            get => Target->flags;
            set => Target->flags = value;
        }

        public long Duration => Target->duration;

        public double DurationSeconds => Duration / Clock.TimeBaseMicros;

        public long StartTime => Target->start_time;

        public string? Url => Target->url is not null
            ? Helpers.PtrToString(Target->url)
            : default;

        public int FindBestStream(AVMediaType mediaType, int wantedStreamIndex, int relatedStreamIndex) =>
            ffmpeg.av_find_best_stream(Target, mediaType, wantedStreamIndex, relatedStreamIndex, null, 0);

        public int FindBestVideoStream(int wantedStreamIndex) =>
            FindBestStream(AVMediaType.AVMEDIA_TYPE_VIDEO, wantedStreamIndex, -1);

        public int FindBestAudioStream(int wantedStreamIndex, int relatedStreamIndex) =>
            FindBestStream(AVMediaType.AVMEDIA_TYPE_AUDIO, wantedStreamIndex, relatedStreamIndex);

        public int FindBestSubtitleStream(int wantedStreamIndex, int relatedStreamIndex) =>
            FindBestStream(AVMediaType.AVMEDIA_TYPE_SUBTITLE, wantedStreamIndex, relatedStreamIndex);

        public int ReadPlay() =>
            ffmpeg.av_read_play(Target);

        public int ReadPause() =>
            ffmpeg.av_read_pause(Target);

        public void DumpFormat(string fileName) =>
            ffmpeg.av_dump_format(Target, 0, fileName, 0);

        public bool IsSeekMethodUnknown =>
            Address.IsNotNull() &&
            InputFormat.IsNotNull() &&
            InputFormat!.Flags.HasFlag(Constants.SeekMethodUnknownFlags) &&
            InputFormat.Target->read_seek.Pointer.IsNull();

        public bool IsRealTime
        {
            get
            {
                var formatNames = InputFormat?.ShortNames?.Select(c => c.ToUpperInvariant()) ?? Array.Empty<string>();
                if (formatNames.Any(c => c == "RTP" || c == "RTSP" || c == "SDP"))
                    return true;

                var url = Url?.ToUpperInvariant() ?? string.Empty;
                var isRealtimeProtocol = url.StartsWith("RTP:", StringComparison.Ordinal) || url.StartsWith("UDP:", StringComparison.Ordinal);
                return IO.IsNotNull() && isRealtimeProtocol;
            }
        }

        public void InjectGlobalSideData() => ffmpeg.av_format_inject_global_side_data(Target);

        public int SeekFile(long seekTargetMin, long seekTarget, long seekTargetMax, int seekFlags = 0) =>
            ffmpeg.avformat_seek_file(Target, -1, seekTargetMin, seekTarget, seekTargetMax, seekFlags);

        public int ReadFrame(out FFPacket packet)
        {
            packet = new FFPacket();
            return ffmpeg.av_read_frame(Target, packet.Target);
        }

        public AVRational GuessFrameRate(FFStream stream) => ffmpeg.av_guess_frame_rate(Target, stream.Target, null);

        public AVRational GuessAspectRatio(FFStream stream, FFFrame? frame = default) =>
            ffmpeg.av_guess_sample_aspect_ratio(Target, stream.Target, frame.IsNotNull() ? frame!.Target : default);

        public void OpenInput(string filePath, FFInputFormat format, FFDictionary formatOptions)
        {
            const string ScanAllPmtsKey = "scan_all_pmts";

            if (filePath is null)
                throw new ArgumentNullException(nameof(filePath));

            if (format is null)
                throw new ArgumentNullException(nameof(format));

            if (formatOptions is null)
                throw new ArgumentNullException(nameof(formatOptions));

            var isScanAllPmtsSet = false;
            if (!formatOptions.ContainsKey(ScanAllPmtsKey))
            {
                formatOptions[ScanAllPmtsKey] = "1";
                isScanAllPmtsSet = true;
            }

            var context = Target;
            var formatOptionsPtr = formatOptions.Target;
            var resultCode = ffmpeg.avformat_open_input(&context, filePath, format.Target, &formatOptionsPtr);
            Update(context);
            format.Update(context->iformat);
            formatOptions.Update(formatOptionsPtr);

            if (isScanAllPmtsSet)
                formatOptions.Remove(ScanAllPmtsKey);

            if (resultCode < 0)
                throw new FFmpegException(resultCode, $"Unable to open input '{filePath}'");
        }

        public int MatchStreamSpecifier(FFStream stream, string specifier) =>
            ffmpeg.avformat_match_stream_specifier(Target, stream.Target, specifier);

        /// <summary>
        /// Port of check_stream_specifier.
        /// Returns 0 for no match, 1 for match and a negative number on error.
        /// </summary>
        /// <param name="stream">The associated stream.</param>
        /// <param name="specifier">The specifier string.</param>
        /// <returns>A non-negative number on success. A negative error code on failure.</returns>
        public int CheckStreamSpecifier(FFStream stream, string specifier)
        {
            var resultCode = MatchStreamSpecifier(stream, specifier);
            if (resultCode < 0)
                this.LogError($"Invalid stream specifier: {specifier}.");

            return resultCode;
        }

        /// <summary>
        /// Port of filter_codec_opts.
        /// </summary>
        /// <param name="allOptions"></param>
        /// <param name="codecId"></param>
        /// <param name="stream"></param>
        /// <param name="codec"></param>
        /// <returns></returns>
        public FFDictionary FilterCodecOptions(
            StringDictionary allOptions,
            AVCodecID codecId,
            FFStream stream,
            FFCodec? codec)
        {

            var filteredOptions = new FFDictionary();

            int optionFlags = Target->oformat is not null
                ? ffmpeg.AV_OPT_FLAG_ENCODING_PARAM
                : ffmpeg.AV_OPT_FLAG_DECODING_PARAM;

            if (codec.IsNull())
            {
                codec = Target->oformat is not null
                    ? FFCodec.FromEncoderId(codecId)
                    : FFCodec.FromDecoderId(codecId);
            }


            // -codec:a:1 ac3
            // option:mediatype:streamindex value
            // option:mediatype
            // option

            var prefix = string.Empty;
            switch (stream.CodecParameters.CodecType)
            {
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    prefix = "v";
                    optionFlags |= ffmpeg.AV_OPT_FLAG_VIDEO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    prefix = "a";
                    optionFlags |= ffmpeg.AV_OPT_FLAG_AUDIO_PARAM;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    prefix = "s";
                    optionFlags |= ffmpeg.AV_OPT_FLAG_SUBTITLE_PARAM;
                    break;
            }

            var semicolonSeprator = new char[] { ':' };
            foreach (var t in allOptions)
            {
                var keyParts = t.Key.Split(semicolonSeprator, 2);
                var optionName = keyParts[0];
                var specifier = keyParts.Length > 1 ? keyParts[1] : null;

                var checkResult = specifier is not null
                    ? CheckStreamSpecifier(stream, specifier)
                    : -1;

                if (checkResult <= 0)
                    continue;

                if (FFMediaClass.Codec.HasOption(optionName, optionFlags) || codec.IsNull() ||
                    codec!.PrivateClass.HasOption(optionName, optionFlags))
                {
                    filteredOptions[optionName] = t.Value;
                }
                else if (prefix.Length > 0 && optionName.Length > 1 && optionName.StartsWith(prefix, StringComparison.Ordinal) &&
                    FFMediaClass.Codec.HasOption(optionName[1..], optionFlags))
                {
                    filteredOptions[optionName[1..]] = t.Value;
                }
            }

            return filteredOptions;
        }

        public void FindStreamInfo(StringDictionary codecOptions)
        {
            var perStreamOptionsList = FindStreamInfoOptions(codecOptions);
            var perStreamOptions = (AVDictionary**)ffmpeg.av_mallocz_array((ulong)perStreamOptionsList.Count, (ulong)sizeof(IntPtr));
            for (var optionIndex = 0; optionIndex < perStreamOptionsList.Count; optionIndex++)
                perStreamOptions[optionIndex] = perStreamOptionsList[optionIndex].Target;

            var resultCode = ffmpeg.avformat_find_stream_info(Target, perStreamOptions);
            ffmpeg.av_freep(&perStreamOptions);

            foreach (var optionsDictionary in perStreamOptionsList)
                optionsDictionary.Release();

            if (resultCode < 0)
                throw new FFmpegException(resultCode, "Unable to find codec paramenters from per-stream options.");
        }

        protected override unsafe void ReleaseInternal(AVFormatContext* target) =>
            ffmpeg.avformat_close_input(&target);

        /// <summary>
        /// Port of setup_find_stream_info_opts.
        /// Gets an array of dictionaries, each associated with a stream, and unsed for calling
        /// <see cref="ffmpeg.avformat_find_stream_info(AVFormatContext*, AVDictionary**)"/>.
        /// </summary>
        /// <param name="inputContext"></param>
        /// <param name="codecOptions"></param>
        /// <returns></returns>
        private IReadOnlyList<FFDictionary> FindStreamInfoOptions(StringDictionary codecOptions)
        {
            var result = new List<FFDictionary>(Streams.Count);
            if (Streams.Count == 0)
                return result;

            for (var i = 0; i < Streams.Count; i++)
            {
                var stream = Streams[i];
                var codecId = stream.CodecParameters.CodecId;
                var streamOptions = FilterCodecOptions(codecOptions, codecId, stream, null);
                result.Add(streamOptions);
            }

            return result;
        }
    }
}
