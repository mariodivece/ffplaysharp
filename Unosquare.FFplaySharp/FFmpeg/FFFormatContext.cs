namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFormatContext : UnmanagedCountedReference<AVFormatContext>
    {
        public FFFormatContext([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.avformat_alloc_context());
            Streams = new(this);
            Chapters = new(this);
        }

        public AVIOInterruptCB_callback_func InterruptCallback
        {
            get => Pointer->interrupt_callback.callback;
            set => Pointer->interrupt_callback.callback = value;
        }

        public StreamCollection Streams { get; }

        public ChapterCollection Chapters { get; }

        public FFInputFormat InputFormat => Pointer->iformat != null
            ? new(Pointer->iformat)
            : null;

        public FFIOContext IO => Pointer->pb != null
            ? new(Pointer->pb)
            : null;

        public IReadOnlyDictionary<string, string> Metadata =>
            FFDictionary.Extract(Pointer->metadata);

        public int Flags
        {
            get => Pointer->flags;
            set => Pointer->flags = value;
        }

        public long Duration => Pointer->duration;

        public double DurationSeconds => Duration / Clock.TimeBaseMicros;

        public long StartTime => Pointer->start_time;

        public string Url => Pointer->url != null
            ? Helpers.PtrToString(Pointer->url)
            : null;

        public int FindBestStream(AVMediaType mediaType, int wantedStreamIndex, int relatedStreamIndex) =>
            ffmpeg.av_find_best_stream(Pointer, mediaType, wantedStreamIndex, relatedStreamIndex, null, 0);

        public int FindBestVideoStream(int wantedStreamIndex) =>
            FindBestStream(AVMediaType.AVMEDIA_TYPE_VIDEO, wantedStreamIndex, -1);

        public int FindBestAudioStream(int wantedStreamIndex, int relatedStreamIndex) =>
            FindBestStream(AVMediaType.AVMEDIA_TYPE_AUDIO, wantedStreamIndex, relatedStreamIndex);

        public int FindBestSubtitleStream(int wantedStreamIndex, int relatedStreamIndex) =>
            FindBestStream(AVMediaType.AVMEDIA_TYPE_SUBTITLE, wantedStreamIndex, relatedStreamIndex);

        public int ReadPlay() =>
            ffmpeg.av_read_play(Pointer);

        public int ReadPause() =>
            ffmpeg.av_read_pause(Pointer);

        public bool IsSeekMethodUnknown =>
            IsNull == false &&
            InputFormat != null &&
            InputFormat.Flags.HasFlag(Constants.SeekMethodUnknownFlags) &&
            InputFormat.Pointer->read_seek.Pointer.IsNull();

        public bool IsRealTime
        {
            get
            {
                var inputFormatName = InputFormat.Name;
                if (inputFormatName == "rtp" || inputFormatName == "rtsp" || inputFormatName == "sdp")
                    return true;

                var url = Url?.ToLowerInvariant();
                url = string.IsNullOrWhiteSpace(url) ? string.Empty : url;

                if (IO != null && (url.StartsWith("rtp:") || url.StartsWith("udp:")))
                    return true;

                return false;
            }
        }

        public void InjectGlobalSideData() => ffmpeg.av_format_inject_global_side_data(Pointer);

        public int SeekFile(long seekTargetMin, long seekTarget, long seekTargetMax, int seekFlags = 0) =>
            ffmpeg.avformat_seek_file(Pointer, -1, seekTargetMin, seekTarget, seekTargetMax, seekFlags);

        public int ReadFrame(out FFPacket packet)
        {
            packet = new FFPacket();
            return ffmpeg.av_read_frame(Pointer, packet.Pointer);
        }

        public FFProgram FindProgramByStream(int streamIndex)
        {
            var program = ffmpeg.av_find_program_from_stream(Pointer, null, streamIndex);
            return program != null ? new(program) : null;
        }

        public AVRational GuessFrameRate(FFStream stream) => ffmpeg.av_guess_frame_rate(Pointer, stream.Pointer, null);

        public AVRational GuessAspectRatio(FFStream stream, FFFrame frame) =>
            ffmpeg.av_guess_sample_aspect_ratio(Pointer, stream.Pointer, frame != null ? frame.Pointer : null);

        public int OpenInput(string filePath, FFInputFormat format, FFDictionary formatOptions)
        {
            const string ScanAllPmtsKey = "scan_all_pmts";

            var isScanAllPmtsSet = false;
            if (!formatOptions.ContainsKey(ScanAllPmtsKey))
            {
                formatOptions[ScanAllPmtsKey] = "1";
                isScanAllPmtsSet = true;
            }

            var context = Pointer;
            var formatOptionsPtr = formatOptions.Pointer;
            var resultCode = ffmpeg.avformat_open_input(&context, filePath, format.Pointer, &formatOptionsPtr);
            Update(context);
            formatOptions.Update(formatOptionsPtr);

            if (isScanAllPmtsSet)
                formatOptions.Remove(ScanAllPmtsKey);

            return resultCode;
        }

        public int FindStreamInfo(StringDictionary codecOptions)
        {
            var perStreamOptionsList = FindStreamInfoOptions(codecOptions);
            var perStreamOptions = (AVDictionary**)ffmpeg.av_mallocz_array((ulong)perStreamOptionsList.Count, (ulong)sizeof(IntPtr));
            for (var optionIndex = 0; optionIndex < perStreamOptionsList.Count; optionIndex++)
                perStreamOptions[optionIndex] = perStreamOptionsList[optionIndex].Pointer;

            var resultCode = ffmpeg.avformat_find_stream_info(Pointer, perStreamOptions);
            ffmpeg.av_freep(&perStreamOptions);

            foreach (var optionsDictionary in perStreamOptionsList)
                optionsDictionary.Release();

            return resultCode;
        }

        protected override unsafe void ReleaseInternal(AVFormatContext* pointer) =>
            ffmpeg.avformat_close_input(&pointer);

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
                var streamOptions = Helpers.FilterCodecOptions(codecOptions, codecId, this, stream, null);
                result.Add(streamOptions);
            }

            return result;
        }
    }
}
