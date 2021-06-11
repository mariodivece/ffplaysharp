namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg;
    using FFmpeg.AutoGen;

    public unsafe abstract class FilteringMediaComponent : MediaComponent
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
                         FFFilterContext inputFilterContext, FFFilterContext outputFilterContext)
        {
            var resultCode = 0;
            var initialFilterCount = FilterGraph.Filters.Count;

            if (!string.IsNullOrWhiteSpace(filterGraphLiteral))
            {
                var output = new FFFilterInOut
                {
                    Name = "in",
                    Filter = inputFilterContext,
                    PadIndex = 0,
                    Next = null
                };

                var input = new FFFilterInOut
                {
                    Name = "out",
                    Filter = outputFilterContext,
                    PadIndex = 0,
                    Next = null
                };

                resultCode = FilterGraph.ParseLiteral(filterGraphLiteral, input, output);
                if (resultCode < 0)
                {
                    output.Release();
                    input.Release();
                    goto fail;
                }
            }
            else
            {
                if ((resultCode = FFFilterContext.Link(inputFilterContext, outputFilterContext)) < 0)
                    goto fail;
            }

            // Reorder the filters to ensure that inputs of the custom filters are merged first
            for (var i = 0; i < FilterGraph.Filters.Count - initialFilterCount; i++)
                FilterGraph.Filters.Swap(i, i + initialFilterCount);

            resultCode = FilterGraph.Commit();

        fail:
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
