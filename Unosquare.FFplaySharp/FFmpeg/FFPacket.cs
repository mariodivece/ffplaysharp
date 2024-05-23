namespace FFmpeg;

public unsafe sealed class FFPacket : CountedReference<AVPacket>, ISerialGroupable
{
    public FFPacket([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
        : this(ffmpeg.av_packet_alloc(), filePath, lineNumber)
    {
        // placeholder
    }

    private FFPacket(AVPacket* pointer, string? filePath, int? lineNumber = default)
        : base(filePath, lineNumber)
    {
        UpdatePointer(pointer);
    }

    public static int StructureSize { get; } = Marshal.SizeOf<AVPacket>();

    public FFPacket? Next { get; set; }

    public int GroupIndex { get; set; }

    public bool IsFlushPacket { get; private set; }

    public int StreamIndex
    {
        get => Reference->stream_index;
        set => Reference->stream_index = value;
    }

    public int Size
    {
        get => Reference->size;
        set => Reference->size = value;
    }

    public long DurationUnits
    {
        get => Reference->duration;
        set => Reference->duration = value;
    }

    public byte* Data
    {
        get => Reference->data;
        set => Reference->data = value;
    }

    public bool HasData => !IsEmpty && Data is not null;

    public static FFPacket CreateFlushPacket()
    {
        var packet = new FFPacket()
        {
            Size = 0,
            IsFlushPacket = true
        };

        packet.Data = (byte*)packet.Reference;
        return packet;
    }

    public static FFPacket CreateNullPacket(int streamIndex) => new()
    {
        Data = default,
        Size = 0,
        StreamIndex = streamIndex,
        DurationUnits = 0
    };

    public static FFPacket Clone(AVPacket* packet, [CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default)
    {
        if (packet is null)
            throw new ArgumentNullException(nameof(packet));

        var copy = ffmpeg.av_packet_clone(packet);
        return new FFPacket(copy, filePath, lineNumber);
    }

    public FFPacket Clone([CallerFilePath] string? filePath = default, [CallerLineNumber] int? lineNumber = default) => IsEmpty
        ? throw new InvalidOperationException("Cannot clone a null packet pointer")
        : new(ffmpeg.av_packet_clone(this), filePath, lineNumber);

    public long Pts => Reference->pts.IsValidPts()
        ? Reference->pts
        : Reference->dts;

    protected override void ReleaseNative(AVPacket* target) =>
        ffmpeg.av_packet_free(&target);
}
