namespace FFmpeg;

public unsafe sealed class FFPacket : UnmanagedCountedReference<AVPacket>, ISerialGroupable
{
    public FFPacket([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : this(ffmpeg.av_packet_alloc(), filePath, lineNumber)
    {
        // placeholder
    }

    private FFPacket(AVPacket* pointer, string? filePath, int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        Update(pointer);
    }

    public static int StructureSize { get; } = Marshal.SizeOf<AVPacket>();

    public FFPacket? Next { get; set; }

    public int GroupIndex { get; set; }

    public bool IsFlushPacket { get; private set; }

    public int StreamIndex
    {
        get => Pointer->stream_index;
        set => Pointer->stream_index = value;
    }

    public int Size
    {
        get => Pointer->size;
        set => Pointer->size = value;
    }

    public long DurationUnits
    {
        get => Pointer->duration;
        set => Pointer->duration = value;
    }

    public byte* Data
    {
        get => Pointer->data;
        set => Pointer->data = value;
    }

    public bool HasData => Address.IsNotNull() && Data is not null;

    public static FFPacket CreateFlushPacket()
    {
        var packet = new FFPacket()
        {
            Size = 0,
            IsFlushPacket = true
        };

        packet.Data = (byte*)packet.Pointer;
        return packet;
    }

    public static FFPacket CreateNullPacket(int streamIndex) => new()
    {
        Data = null,
        Size = 0,
        StreamIndex = streamIndex
    };

    public static FFPacket Clone(AVPacket* packet, [CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
    {
        if (packet is null)
            throw new ArgumentNullException(nameof(packet));

        var copy = ffmpeg.av_packet_clone(packet);
        return new FFPacket(copy, filePath, lineNumber);
    }

    public FFPacket Clone([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default) => Address.IsNull()
        ? throw new InvalidOperationException("Cannot clone a null packet pointer")
        : new(ffmpeg.av_packet_clone(Pointer), filePath, lineNumber);

    public long Pts => Pointer->pts.IsValidPts()
        ? Pointer->pts
        : Pointer->dts;

    protected override void ReleaseInternal(AVPacket* pointer) =>
        ffmpeg.av_packet_free(&pointer);
}
