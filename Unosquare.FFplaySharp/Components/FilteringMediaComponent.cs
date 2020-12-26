namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;

    public abstract unsafe class FilteringMediaComponent : MediaComponent
    {
        public AVFilterGraph* FilterGraph = null;
        public AVFilterContext* InputFilter = null;
        public AVFilterContext* OutputFilter = null;

        protected FilteringMediaComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        protected static int MaterializeFilterGraph(AVFilterGraph* filterGraph, string filterGraphLiteral,
                         AVFilterContext* inputFilterContext, AVFilterContext* outputFilterContext)
        {
            var ret = 0;
            var initialFilterCount = filterGraph->nb_filters;
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

                if ((ret = ffmpeg.avfilter_graph_parse_ptr(filterGraph, filterGraphLiteral, &inputs, &outputs, null)) < 0)
                    goto fail;
            }
            else
            {
                if ((ret = ffmpeg.avfilter_link(inputFilterContext, 0, outputFilterContext, 0)) < 0)
                    goto fail;
            }

            // Reorder the filters to ensure that inputs of the custom filters are merged first
            for (var i = 0; i < filterGraph->nb_filters - initialFilterCount; i++)
                Helpers.FFSWAP(filterGraph->filters, i, i + (int)initialFilterCount);

            ret = ffmpeg.avfilter_graph_config(filterGraph, null);

        fail:
            ffmpeg.avfilter_inout_free(&outputs);
            ffmpeg.avfilter_inout_free(&inputs);
            return ret;
        }

    }
}
