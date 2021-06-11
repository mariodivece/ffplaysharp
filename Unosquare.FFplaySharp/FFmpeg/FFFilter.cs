namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFFilter : UnmanagedReference<AVFilter>
    {
        private FFFilter(AVFilter* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public static FFFilter FromName(string name)
        {
            var pointer = ffmpeg.avfilter_get_by_name(name);
            return pointer != null ? new FFFilter(pointer) : null;
        }
    }
}
