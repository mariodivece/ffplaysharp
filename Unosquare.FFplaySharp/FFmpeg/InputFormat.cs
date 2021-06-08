namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading.Tasks;
    using Unosquare.FFplaySharp.Primitives;

    public class InputFormat : UnmanagedReference<AVInputFormat>
    {
        public InputFormat([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
            : base(filePath, lineNumber)
        {
            // placeholder
        }

        protected override unsafe void ReleaseInternal(AVInputFormat* pointer)
        {
            throw new NotImplementedException();
        }
    }
}
