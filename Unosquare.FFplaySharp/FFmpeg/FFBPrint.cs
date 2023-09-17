using FFmpeg.AutoGen;

namespace FFmpeg;

internal unsafe class FFBPrint : CountedReference<AVBPrint>
{
    private static readonly nint ReservedFieldOffset = sizeof(nint) + 3 * sizeof(uint);

    public FFBPrint([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default) 
        : base(filePath, lineNumber)
    {
        Update(AllocateAutoAVBPrint());
    }

    public string Contents
    {
        get
        {
            if (this.IsNull())
                return string.Empty;

            var bpStruct = Marshal.PtrToStructure<AVBPrintExtended>(Address);
            return Helpers.PtrToString(bpStruct.str) ?? string.Empty;
        }
    }

    private static unsafe AVBPrint* AllocateAutoAVBPrint()
    {
        // https://ffmpeg.org/doxygen/1.0/bprint_8h-source.html
        const int StructurePadding = 1024;
        var bpStructAddress = ffmpeg.av_mallocz(StructurePadding);
        var bStruct = default(AVBPrintExtended);

        bStruct.len = 0;
        bStruct.size = 1;
        bStruct.size_max = uint.MaxValue - 1;
        bStruct.reserved_internal_buffer = 0;

        // point at the address of the reserved_internal_buffer
        bStruct.str = (byte*)((nint)bpStructAddress + ReservedFieldOffset);

        Marshal.StructureToPtr(bStruct, (nint)bpStructAddress, true);
        return (AVBPrint*)bpStructAddress;
    }

    protected override unsafe void ReleaseInternal(AVBPrint* target)
    {
        var bpStruct = Marshal.PtrToStructure<AVBPrintExtended>((nint)target);

        var isAllocated = target + ReservedFieldOffset != bpStruct.str;

        if (isAllocated)
            ffmpeg.av_freep(&bpStruct.str);

        ffmpeg.av_freep(&target);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AVBPrintExtended
    {
        public byte* str;
        public uint len;
        public uint size;
        public uint size_max;
        public byte reserved_internal_buffer;
    }
}
