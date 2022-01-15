namespace FFmpeg;

public unsafe sealed class FFStream : UnmanagedReference<AVStream>
{
    public FFStream(AVStream* pointer)
        : base(pointer)
    {
        // placeholder
    }

    public AVDiscard DiscardFlags
    {
        get => Pointer->discard;
        set => Pointer->discard = value;
    }

    public FFCodecParameters CodecParameters => new(Pointer->codecpar);

    public AVRational TimeBase => Pointer->time_base;

    public long StartTime => Pointer->start_time;

    public int DispositionFlags => Pointer->disposition;

    public FFPacket CloneAttachedPicture()
    {
        var packet = &Pointer->attached_pic;
        return FFPacket.Clone(packet);
    }

    /// <summary>
    /// Port of get_rotation
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    public double ComputeDisplayRotation()
    {
        var displayMatrix = ffmpeg.av_stream_get_side_data(Pointer, AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX, null);
        var theta = displayMatrix != null ? -ComputeMatrixRotation((int*)displayMatrix) : 0d;
        theta -= 360 * Math.Floor(theta / 360 + 0.9 / 360);

        if (Math.Abs(theta - 90 * Math.Round(theta / 90, 0)) > 2)
            ("Odd rotation angle.\n" +
            "If you want to help, upload a sample " +
            "of this file to https://streams.videolan.org/upload/ " +
            "and contact the ffmpeg-devel mailing list. (ffmpeg-devel@ffmpeg.org)").LogWarning();

        return theta;
    }

    /// <summary>
    /// Port of av_display_rotation_get.
    /// </summary>
    /// <param name="matrix"></param>
    /// <returns></returns>
    private static unsafe double ComputeMatrixRotation(int* matrix)
    {
        var scale = new double[2];
        scale[0] = ComputeHypotenuse(matrix[0].ToDouble(), matrix[3].ToDouble());
        scale[1] = ComputeHypotenuse(matrix[1].ToDouble(), matrix[4].ToDouble());

        if (scale[0] == 0.0 || scale[1] == 0.0)
            return double.NaN;

        var rotation = Math.Atan2(matrix[1].ToDouble() / scale[1], matrix[0].ToDouble() / scale[0]) * 180 / Math.PI;

        return -rotation;
    }

    /// <summary>
    /// Port of hypot
    /// </summary>
    /// <param name="s1"></param>
    /// <param name="s2"></param>
    /// <returns></returns>
    private static double ComputeHypotenuse(double s1, double s2) => Math.Sqrt((s1 * s1) + (s2 * s2));
}
