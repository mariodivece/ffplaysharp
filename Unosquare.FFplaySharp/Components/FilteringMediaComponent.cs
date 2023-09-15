namespace Unosquare.FFplaySharp.Components;

public abstract class FilteringMediaComponent : MediaComponent
{
    protected FFFilterGraph FilterGraph;
    protected FFFilterContext InputFilter;
    protected FFFilterContext OutputFilter;

    protected FilteringMediaComponent(MediaContainer container)
        : base(container)
    {
        // placeholder
    }

    protected AVRational OutputFilterTimeBase => OutputFilter.TimeBase;

    protected void MaterializeFilterGraph(string? filterGraphLiteral,
        FFFilterContext inputFilterContext,
        FFFilterContext outputFilterContext)
    {
        var initialFilterCount = FilterGraph.Filters.Count;

        if (!string.IsNullOrWhiteSpace(filterGraphLiteral))
        {
            var output = new FFFilterInOut
            {
                Name = "in",
                Filter = inputFilterContext,
                PadIndex = 0,
                Next = default
            };

            var input = new FFFilterInOut
            {
                Name = "out",
                Filter = outputFilterContext,
                PadIndex = 0,
                Next = default
            };

            try
            {
                FilterGraph.ParseLiteral(filterGraphLiteral, input, output);
            }
            catch
            {
                output.Release();
                input.Release();
                throw;
            }
        }
        else
        {
            FFFilterContext.Link(inputFilterContext, outputFilterContext);
        }

        // Reorder the filters to ensure that inputs of the custom filters are merged first
        for (var i = 0; i < FilterGraph.Filters.Count - initialFilterCount; i++)
            FilterGraph.Filters.Swap(i, i + initialFilterCount);

        FilterGraph.Commit();
    }

    protected int EnqueueFilteringFrame(FFFrame decodedFrame) =>
        InputFilter.AddSourceFrame(decodedFrame);

    protected int DequeueFilteringFrame(FFFrame decodedFrame) =>
        OutputFilter.GetSinkFrame(decodedFrame);

    protected void ReleaseFilterGraph()
    {
        FilterGraph?.Release();
        FilterGraph = default;
        InputFilter = default;
        OutputFilter = default;
    }

    protected void ReallocateFilterGraph()
    {
        ReleaseFilterGraph();
        FilterGraph = new()
        {
            ThreadCount = Container.Options.FilteringThreadCount
        };
    }
}
