using Unosquare.FFplaySharp.Interop;

namespace FFmpeg;

public unsafe sealed class FFStream : NativeReference<AVStream>
{
    public FFStream(AVStream* target, FFFormatContext formatContext)
        : base(target)
    {
        FormatContext = formatContext;
    }

    public AVDiscard DiscardFlags
    {
        get => Reference->discard;
        set => Reference->discard = value;
    }

    public FFFormatContext FormatContext { get; }

    public FFCodecParameters CodecParameters => new(Reference->codecpar);

    public int Index => Reference->index;

    public AVRational TimeBase => Reference->time_base;

    public long StartTime => Reference->start_time;

    public int DispositionFlags => Reference->disposition;

    public IReadOnlyList<FFProgram> FindPrograms()
    {
        var result = new List<FFProgram>(16);
        AVProgram* program = default;
        while ((program = ffmpeg.av_find_program_from_stream(FormatContext, program, Index)) is not null)
            result.Add(new(program));

        return result;
    }

    public FFPacket CloneAttachedPicture()
    {
        var packet = &Reference->attached_pic;
        return FFPacket.Clone(packet);
    }

    /// <summary>
    /// Port of get_rotation
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    [Obsolete("Use alternative method with same name that takes in a frame as a parameter.")]
    public double ComputeDisplayRotation()
    {
        var displayMatrix = ffmpeg.av_stream_get_side_data(this, AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX, null);
        var theta = displayMatrix is not null ? -ComputeMatrixRotation((int*)displayMatrix) : 0d;
        theta -= 360 * Math.Floor(theta / 360 + 0.9 / 360);

        if (Math.Abs(theta - 90 * Math.Round(theta / 90, 0)) > 2)
            ("Odd rotation angle.\n" +
            "If you want to help, upload a sample " +
            "of this file to https://streams.videolan.org/upload/ " +
            "and contact the ffmpeg-devel mailing list. (ffmpeg-devel@ffmpeg.org)").LogWarning();

        return theta;
    }

    public double ComputeDisplayRotation(FFFrame decoderFrame)
    {
        NativeArgumentException.ThrowIfNullOrEmpty(decoderFrame);

        int* displayMatrix = null;
        var sd = ffmpeg.av_frame_get_side_data(decoderFrame, AVFrameSideDataType.AV_FRAME_DATA_DISPLAYMATRIX);
        if (sd is not null)
            displayMatrix = (int*)sd->data;

        if (displayMatrix is null)
        {
            var psd = ffmpeg.av_packet_side_data_get(
                CodecParameters.Reference->coded_side_data,
                CodecParameters.Reference->nb_coded_side_data,
                AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX);

            if (psd is not null)
                displayMatrix = (int*)psd->data;
        }

        var theta = displayMatrix is not null ? -ComputeMatrixRotation((int*)displayMatrix) : 0d;
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
