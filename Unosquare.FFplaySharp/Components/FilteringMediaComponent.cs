namespace Unosquare.FFplaySharp.Components
{
    using FFmpeg.AutoGen;

    public abstract unsafe class FilteringMediaComponent : MediaComponent
    {
        protected FilteringMediaComponent(MediaContainer container)
            : base(container)
        {
            // placeholder
        }

        public AVFilterContext* InputFilter;
        public AVFilterContext* OutputFilter;
    }
}
