namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Runtime.CompilerServices;
    using Unosquare.FFplaySharp;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFilterGraph : UnmanagedCountedReference<AVFilterGraph>
    {
        public FFFilterGraph([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            Update(ffmpeg.avfilter_graph_alloc());
        }

        public FilterCollection Filters => new(this);

        public int ThreadCount
        {
            get => Pointer->nb_threads;
            set => Pointer->nb_threads = value;
        }

        public string SoftwareScalerOptions
        {
            get => Helpers.PtrToString(Pointer->scale_sws_opts);
            set => Pointer->scale_sws_opts = value == null ? null : ffmpeg.av_strdup(value);
        }

        public int ParseLiteral(string graphLiteral, AVFilterInOut** inputs, AVFilterInOut** outputs) =>
            ffmpeg.avfilter_graph_parse_ptr(Pointer, graphLiteral, inputs, outputs, null);

        /// <summary>
        /// Port of FFSWAP
        /// </summary>
        /// <param name="indexA"></param>
        /// <param name="indexB"></param>
        public void SwapFilters(int indexA, int indexB)
        {
            var tempItem = Pointer->filters[indexB];
            Pointer->filters[indexB] = Pointer->filters[indexA];
            Pointer->filters[indexA] = tempItem;
        }

        public int Commit() =>
            ffmpeg.avfilter_graph_config(Pointer, null);

        public int SetOption(string key, string value) =>
            ffmpeg.av_opt_set(Pointer, key, value, 0);

        protected override unsafe void ReleaseInternal(AVFilterGraph* pointer) =>
            ffmpeg.avfilter_graph_free(&pointer);
    }
}
