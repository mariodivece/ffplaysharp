namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg;
    using FFmpeg.AutoGen;

    public abstract unsafe class FilteringMediaComponent : MediaComponent
    {
        protected FFFilterGraph FilterGraph = null;
        protected FFFilterContext InputFilter = null;
        protected FFFilterContext OutputFilter = null;

        protected FilteringMediaComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        protected AVRational OutputFilterTimeBase => OutputFilter.TimeBase;

        protected int MaterializeFilterGraph(string filterGraphLiteral,
                         AVFilterContext* inputFilterContext, AVFilterContext* outputFilterContext)
        {
            var resultCode = 0;
            var initialFilterCount = FilterGraph.Filters.Count;
            AVFilterInOut* outputs = null;
            AVFilterInOut* inputs = null;

            if (!string.IsNullOrWhiteSpace(filterGraphLiteral))
            {
                outputs = ffmpeg.avfilter_inout_alloc();
                outputs->name = ffmpeg.av_strdup("in");
                outputs->filter_ctx = inputFilterContext;
                outputs->pad_idx = 0;
                outputs->next = null;

                inputs = ffmpeg.avfilter_inout_alloc();
                inputs->name = ffmpeg.av_strdup("out");
                inputs->filter_ctx = outputFilterContext;
                inputs->pad_idx = 0;
                inputs->next = null;

                resultCode = FilterGraph.ParseLiteral(filterGraphLiteral, &inputs, &outputs);
                if (resultCode < 0)
                    goto fail;
            }
            else
            {
                if ((resultCode = ffmpeg.avfilter_link(inputFilterContext, 0, outputFilterContext, 0)) < 0)
                    goto fail;
            }

            // Reorder the filters to ensure that inputs of the custom filters are merged first
            for (var i = 0; i < FilterGraph.Filters.Count - initialFilterCount; i++)
                FilterGraph.SwapFilters(i, i + initialFilterCount);

            resultCode = FilterGraph.Commit();

        fail:
            ffmpeg.avfilter_inout_free(&outputs);
            ffmpeg.avfilter_inout_free(&inputs);
            return resultCode;
        }




        protected int EnqueueInputFilter(AVFrame* decodedFrame) => InputFilter.AddFrame(decodedFrame);

        protected int DequeueOutputFilter(AVFrame* decodedFrame) => OutputFilter.GetSinkFlags(decodedFrame);

        protected void ReleaseFilterGraph()
        {
            FilterGraph?.Release();
            FilterGraph = null;
            InputFilter = null;
            OutputFilter = null;
        }

        protected void ReallocateFilterGraph()
        {
            ReleaseFilterGraph();
            FilterGraph = new();
            FilterGraph.ThreadCount = Container.Options.FilteringThreadCount;
        }
    }
}
