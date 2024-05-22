namespace FFmpeg;

public unsafe sealed class FFProgram : NativeReference<AVProgram>
{
    public FFProgram(AVProgram* target)
        : base(target)
    {
        // placeholder
    }

    public int StreamIndexCount => Convert.ToInt32(Reference->nb_stream_indexes);

    public IReadOnlyList<int> StreamIndices
    {
        get
        {
            var result = new List<int>(StreamIndexCount);
            for (var i = 0; i < StreamIndexCount; i++)
                result.Add(Convert.ToInt32(Reference->stream_index[i]));

            return result;
        }
    }
}
