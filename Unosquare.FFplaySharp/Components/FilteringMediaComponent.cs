namespace Unosquare.FFplaySharp.Components;

public abstract class FilteringMediaComponent : MediaComponent
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

    protected void MaterializeFilterGraph(
        string filterGraphLiteral, FFFilterContext inputFilterContext, FFFilterContext outputFilterContext)
    {
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
