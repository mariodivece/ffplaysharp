using FFmpeg.AutoGen;

namespace FFmpeg;

internal unsafe class FFBPrint : CountedReference<AVBPrint>
{
    public FFBPrint(uint maxSize = 2048, [CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default) 
        : base(filePath, lineNumber)
    {
        Update(AllocateAVBPrint(maxSize));
    }

    public string Contents
    {
        get
        {
            if (this.IsNull())
                return string.Empty;

            var bpStruct = Marshal.PtrToStructure<AVBPrintExtended>(Address);
            return Helpers.PtrToString((byte*)bpStruct.str) ?? string.Empty;
        }
    }

    private static unsafe AVBPrint* AllocateAVBPrint(uint maxSize)
    {
        // https://ffmpeg.org/doxygen/1.0/bprint_8h-source.html
        const int StructurePadding = 1024;
        var bpStructAddress = ffmpeg.av_mallocz(StructurePadding);
        var bStruct = default(AVBPrintExtended);

        bStruct.str = ffmpeg.av_mallocz(maxSize);
        bStruct.len = 0;
        bStruct.size = maxSize;
        bStruct.size_max = maxSize;
        bStruct.reserved_internal_buffer = 0;

        Marshal.StructureToPtr(bStruct, (nint)bpStructAddress, true);
        return (AVBPrint*)bpStructAddress;
    }

    protected override unsafe void ReleaseInternal(AVBPrint* target)
    {
        var bpStruct = Marshal.PtrToStructure<AVBPrintExtended>((nint)target);
        ffmpeg.av_freep(&bpStruct.str);
        ffmpeg.av_freep(&target);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AVBPrintExtended
    {
        public void* str;
        public uint len;
        public uint size;
        public uint size_max;
        public byte reserved_internal_buffer;
    }
}
