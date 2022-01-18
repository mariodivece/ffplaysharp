namespace FFmpeg;

public unsafe sealed class RescalerContext : CountedReference<SwsContext>
{
    public RescalerContext([CallerFilePath] string filePath = default, [CallerLineNumber] int lineNumber = default)
        : base(filePath, lineNumber)
    {
        Update(ffmpeg.sws_alloc_context());
    }


    public void Reallocate(
        int inW, int inH, AVPixelFormat inFormat, int outW, int outH, AVPixelFormat outFormat, int interpolationFlags = Constants.RescalerInterpolation)
    {
        var updatedPointer = ffmpeg.sws_getCachedContext(
            Target, inW, inH, inFormat, outW, outH, outFormat, interpolationFlags, null, null, null);

        if (updatedPointer is null)
        {
            Release();
            return;
        }

        Update(updatedPointer);
    }

    public int SetOption(string key, string value) =>
        ffmpeg.av_opt_set(Target, key, value, 0);

    public int Convert(byte*[] inPlanes, int[] inStrides, int inH, IntPtr outPixels, int outStride)
    {
        var targetStride = new[] { outStride };
        var targetScan = default(byte_ptrArray8);
        targetScan[0] = (byte*)outPixels.ToPointer();

        return ffmpeg.sws_scale(Target, inPlanes, inStrides, 0, inH, targetScan, targetStride);
    }

    protected override unsafe void ReleaseInternal(SwsContext* pointer) =>
        ffmpeg.sws_freeContext(pointer);
}
