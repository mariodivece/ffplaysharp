namespace FFmpeg;

public unsafe sealed class RescalerContext : CountedReference<SwsContext>
{
    public RescalerContext([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : base(ffmpeg.sws_alloc_context(), filePath, lineNumber)
    {
        // placeholder
    }

    public void Reallocate(
        int inW, int inH, AVPixelFormat inFormat, int outW, int outH, AVPixelFormat outFormat, int interpolationFlags = Constants.RescalerInterpolation)
    {
        var updatedPointer = ffmpeg.sws_getCachedContext(
            Reference, inW, inH, inFormat, outW, outH, outFormat, interpolationFlags, null, null, null);

        if (updatedPointer is null)
        {
            Dispose();
            return;
        }

        UpdatePointer(updatedPointer);
    }

    public int SetOption(string key, string value) =>
        ffmpeg.av_opt_set(Reference, key, value, 0);

    public int Convert(byte*[] inPlanes, int[] inStrides, int inH, IntPtr outPixels, int outStride)
    {
        var targetStride = new[] { outStride };
        var targetScan = default(byte_ptrArray8);
        targetScan[0] = (byte*)outPixels.ToPointer();

        return ffmpeg.sws_scale(Reference, inPlanes, inStrides, 0, inH, targetScan, targetStride);
    }

    protected override unsafe void DisposeNative(SwsContext* target) =>
        ffmpeg.sws_freeContext(target);
}
