namespace FFmpeg
{
    using FFmpeg.AutoGen;
    using System;
    using System.Collections.Generic;
    using Unosquare.FFplaySharp.Primitives;

    public unsafe sealed class FFProgram : UnmanagedReference<AVProgram>
    {
        public FFProgram(AVProgram* pointer)
            : base(pointer)
        {
            // placeholder
        }

        public int StreamIndexCount => Convert.ToInt32(Pointer->nb_stream_indexes);

        public IReadOnlyList<int> StreamIndices
        {
            get
            {
                var result = new List<int>(StreamIndexCount);
                for (var i = 0; i < StreamIndexCount; i++)
                    result.Add(Convert.ToInt32(Pointer->stream_index[i]));

                return result;
            }
        }
    }
}
