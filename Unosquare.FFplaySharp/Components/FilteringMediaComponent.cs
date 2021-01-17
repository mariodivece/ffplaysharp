namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;

    public abstract unsafe class FilteringMediaComponent : MediaComponent
    {
        protected AVFilterGraph* FilterGraph = null;
        protected AVFilterContext* InputFilter = null;
        protected AVFilterContext* OutputFilter = null;

        protected FilteringMediaComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        protected int MaterializeFilterGraph(string filterGraphLiteral,
                         AVFilterContext* inputFilterContext, AVFilterContext* outputFilterContext)
        {
            var resultCode = 0;
            var initialFilterCount = (int)FilterGraph->nb_filters;
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

                if ((resultCode = ffmpeg.avfilter_graph_parse_ptr(FilterGraph, filterGraphLiteral, &inputs, &outputs, null)) < 0)
                    goto fail;
            }
            else
            {
                if ((resultCode = ffmpeg.avfilter_link(inputFilterContext, 0, outputFilterContext, 0)) < 0)
                    goto fail;
            }

            // Reorder the filters to ensure that inputs of the custom filters are merged first
            for (var i = 0; i < FilterGraph->nb_filters - initialFilterCount; i++)
                SwapFilters(i, i + initialFilterCount);

            resultCode = ffmpeg.avfilter_graph_config(FilterGraph, null);

        fail:
            ffmpeg.avfilter_inout_free(&outputs);
            ffmpeg.avfilter_inout_free(&inputs);
            return resultCode;
        }


        /// <summary>
        /// Port of FFSWAP
        /// </summary>
        /// <param name="indexA"></param>
        /// <param name="indexB"></param>
        private void SwapFilters(int indexA, int indexB)
        {
            var tempItem = FilterGraph->filters[indexB];
            FilterGraph->filters[indexB] = FilterGraph->filters[indexA];
            FilterGraph->filters[indexA] = tempItem;
        }

        protected void ReleaseFilterGraph()
        {
            var filterGraph = FilterGraph;
            ffmpeg.avfilter_graph_free(&filterGraph);
            FilterGraph = null;
        }
    }
}
