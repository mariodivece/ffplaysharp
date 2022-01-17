namespace Unosquare.FFplaySharp.Primitives;

public sealed class FrameHolder : IDisposable, ISerialGroupable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FrameHolder" /> class.
    /// </summary>
    public FrameHolder()
    {
        Frame = new FFFrame();
    }

    public FFFrame Frame { get; private set; }

    public FFSubtitle Subtitle { get; private set; }

    public int GroupIndex { get; private set; }

    /// <summary>
    /// Gets or sets the Presentation time in seconds.
    /// This is NOT a timestamp in stream units.
    /// </summary>
    public double Time { get; private set; }

    /// <summary>
    /// Gets the estimated duration of the frame in seconds.
    /// </summary>
    public double Duration { get; private set; }

    /// <summary>
    /// Gets the video frame width in pixels.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets the video frame height in pixels.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets whether the frame has been marked as uploaded to the renderer.
    /// </summary>
    public bool IsUploaded { get; private set; }

    /// <summary>
    /// Gets whether the video frame is flipped vertically.
    /// </summary>
    public bool IsPictureVerticalFlipped => Frame.IsNotNull() && Frame.LineSize[0] < 0;

    public long ChannelLayout { get; private set; }

    public bool HasValidTime => !Time.IsNaN();

    public double StartDisplayTime => Subtitle.IsNotNull()
        ? Time + (Subtitle.StartDisplayTime / 1000d)
        : Time;

    public double EndDisplayTime => Subtitle.IsNotNull()
        ? Time + (Subtitle.EndDisplayTime / 1000d)
        : Time + Duration;

    public void MarkUploaded() => IsUploaded = true;

    public void Update(FFFrame sourceFrame, int groupIndex, double time, double duration)
    {
        if (sourceFrame.IsNull())
            throw new ArgumentNullException(nameof(sourceFrame));

        sourceFrame.MoveTo(Frame);
        IsUploaded = false;
        GroupIndex = groupIndex;
        Time = time;
        Duration = duration;
        Width = Frame.Width;
        Height = Frame.Height;
        ChannelLayout = AudioParams.ComputeChannelLayout(Frame);
    }

    public void Update(FFSubtitle sourceFrame, FFCodecContext codecContext, int groupIndex, double time)
    {
        if (codecContext.IsNull())
            throw new ArgumentNullException(nameof(codecContext));

        Subtitle?.Release();
        Subtitle = sourceFrame;
        IsUploaded = false;
        GroupIndex = groupIndex;
        Time = time;
        Duration = (Subtitle.EndDisplayTime - Subtitle.StartDisplayTime) / 1000d;
        Width = codecContext.Width;
        Height = codecContext.Height;
    }

    public void UpdateSubtitleArea(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Reset()
    {
        Frame?.Reset();
        Subtitle?.Release();
        Subtitle = default;
    }

    public void Dispose()
    {
        Frame?.Release();
        Frame = default;

        Subtitle?.Release();
        Subtitle = default;
    }
}
