using System.Windows.Media;

namespace Unosquare.FFplaySharp.Wpf;

internal class WpfPresenter : IPresenter
{
    private const bool UseNativeMethod = false;
    private const bool DropFrames = true;

    private static readonly Duration LockTimeout = new(TimeSpan.FromMilliseconds(0));
    private readonly ThreadedTimer RenderTimer = new(2);
    private PictureParams CurrentPicture = new();
    private WriteableBitmap? TargetBitmap;
    private WavePlayer WavePlayer;
    private long m_HasLockedBuffer = 0;

    public MainWindow? Window { get; init; }

    public MediaContainer Container { get; private set; }

    public IReadOnlyList<AVPixelFormat> PixelFormats { get; } = new[] { AVPixelFormat.AV_PIX_FMT_BGRA };

    private bool HasLockedBuffer
    {
        get => Interlocked.Read(ref m_HasLockedBuffer) != 0;
        set => Interlocked.Exchange(ref m_HasLockedBuffer, value ? 1 : 0);
    }

    public bool Initialize(MediaContainer container)
    {
        Container = container;
        return true;
    }

    public void Start()
    {
        var frameNumber = 0;
        var startNextFrame = default(bool);
        var runtimeStopwatch = new MultimediaStopwatch();
        var frameStopwatch = new MultimediaStopwatch();
        var pictureDuration = default(double?);
        var compensation = default(double);
        var previousElapsed = default(double);
        var elapsedSamples = new List<double>(2048);

        RenderTimer.Elapsed += (s, e) =>
        {
            if (CurrentPicture is null || Container.Video.Frames.IsClosed)
                return;

            if (WavePlayer is not null && !WavePlayer.HasStarted)
                WavePlayer.Start();

            if (Container.IsAtEndOfStream && !Container.Video.Frames.HasPending && Container.Video.HasFinishedDecoding)
            {
                var runtimeClock = runtimeStopwatch.ElapsedSeconds;
                var videoClock = Container.VideoClock.Value;
                var drift = runtimeClock - videoClock;
                Debug.WriteLine($"Done reading and displaying frames. "
                    + $"RT: {runtimeClock:n3} VCLK: {videoClock:n3} DRIFT: {drift:n3}");

                Debug.WriteLine($"Frame Average Elapsed: {elapsedSamples.Average():n2}.");

                RenderTimer.Dispose();
            }

        retry:
            if (!Container.Video.Frames.HasPending)
                return;

            var frame = Container.Video.Frames.WaitPeekShowable();
            if (frame is null)
                return;

            // Handle first incoming frame.
            if (!pictureDuration.HasValue)
            {
                runtimeStopwatch.Restart();
                frameStopwatch.Restart();
                pictureDuration = frame.Duration;
                startNextFrame = false;
            }

            if (startNextFrame)
            {
                startNextFrame = false;
                compensation = pictureDuration.Value - previousElapsed;
                pictureDuration = frame.Duration + compensation;
                if (frame.HasValidTime)
                    Container.UpdateVideoPts(frame.Time, frame.GroupIndex);

                frameNumber++;
#if DEBUG
                Debug.WriteLine($"NUM: {frameNumber,-6} PREV: {previousElapsed * 1000,6:n2} COMP: {compensation * 1000,6:n2} NEXT: {pictureDuration * 1000,6:n2}");
#endif
            }

            if (!frame.IsUploaded && (!DropFrames || pictureDuration > 0))
                RenderBackBuffer(frame);

            if (frameStopwatch.ElapsedSeconds >= pictureDuration)
            {
                Container.Video.Frames.Dequeue();
                startNextFrame = true;
                previousElapsed = frameStopwatch.Restart();
                elapsedSamples.Add(previousElapsed * 1000);
                goto retry;
            }
        };

        RenderTimer.Start();
    }

    public void UpdatePictureSize(int width, int height, AVRational sar)
    {
        var requestedPicture = PictureParams.FromDimensions(width, height, sar);

        if (CurrentPicture.MatchesDimensions(requestedPicture))
            return;

        UiInvoke(() =>
        {
            CurrentPicture = requestedPicture;
            TargetBitmap = CurrentPicture.CreateBitmap();
            RenderOptions.SetBitmapScalingMode(Window!.targetImage, BitmapScalingMode.LowQuality);
            RenderOptions.SetEdgeMode(Window!.targetImage, EdgeMode.Aliased);
            Window.targetImage.SnapsToDevicePixels = true;
            Window!.targetImage.Source = TargetBitmap;
        });
    }

    private unsafe bool RenderBackBuffer(FrameHolder frame)
    {
        if (TargetBitmap is null || HasLockedBuffer)
            return false;

        UiInvoke(() =>
        {
            if (!TargetBitmap.TryLock(LockTimeout))
                return;

            CurrentPicture.Buffer = TargetBitmap.BackBuffer;
            CurrentPicture.Stride = TargetBitmap.BackBufferStride;
            HasLockedBuffer = true;
        }, DispatcherPriority.Loaded);

        if (!HasLockedBuffer)
            return false;

        CopyPictureFrame(Container, frame, CurrentPicture, UseNativeMethod);
        frame.MarkUploaded();

        UiInvokeAsync(() =>
        {
            if (!HasLockedBuffer)
                return;

            TargetBitmap.AddDirtyRect(CurrentPicture.ToRect());
            TargetBitmap.Unlock();
            HasLockedBuffer = false;
        }, DispatcherPriority.Render);

        return true;
    }

    private static unsafe void CopyPictureFrame(MediaContainer container, FrameHolder source, PictureParams target, bool useNativeMethod)
    {
        // When not using filtering, the software scaler will kick in.
        // I don't see when this would be true, but leaving in here just in case.
        if (source.Frame.PixelFormat != target.PixelFormat)
        {
            var convert = container.Video.ConvertContext;

            convert.Reallocate(
                source.Width, source.Height, source.Frame.PixelFormat,
                target.Width, target.Height, target.PixelFormat);

            convert.Convert(
                source.Frame.Data, source.Frame.LineSize, source.Frame.Height,
                target.Buffer, target.Stride);

            return;
        }

        var sourceData = new byte_ptrArray4() { [0] = source.Frame.Data[0] };
        var targetData = new byte_ptrArray4() { [0] = (byte*)target.Buffer };
        var sourceStride = new long_array4() { [0] = source.Frame.LineSize[0] };
        var targetStride = new long_array4() { [0] = target.Stride };

        // This is FFmpeg's way of copying pictures.
        // The av_image_copy_uc_from is slightly faster than
        // av_image_copy_to_buffer or av_image_copy alternatives.
        if (useNativeMethod)
        {
            ffmpeg.av_image_copy_uc_from(
                ref targetData, targetStride,
                in sourceData, sourceStride,
                target.PixelFormat, target.Width, target.Height);

            return;
        }

        // There might be alignment differences. Make sure the minimum
        // stride is chosen.
        var maxLineSize = Math.Min(sourceStride[0], targetStride[0]);

        // If source and target have the same strides, simply copy the bytes directly.
        if (sourceStride[0] == targetStride[0])
        {
            var byteLength = maxLineSize * target.Height;
            Buffer.MemoryCopy(sourceData[0], targetData[0], byteLength, byteLength);
            return;
        }

        // If the source and target differ in strides (byte alignment)
        // we'll need to copy line by line.
        for (var lineIndex = 0; lineIndex < target.Height; lineIndex++)
        {
            Buffer.MemoryCopy(
                sourceData[0] + (lineIndex * sourceStride[0]),
                targetData[0] + (lineIndex * targetStride[0]),
                maxLineSize, maxLineSize);
        }
    }

    private void UiInvoke(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (!TryGetUiDispatcher(out var ui))
            return;

        try
        {
            ui?.Invoke(action, priority);
        }
        catch (TaskCanceledException)
        {
            // ignore
        }
    }

    private void UiInvokeAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (!TryGetUiDispatcher(out var ui))
            return;

        ui?.InvokeAsync(action, priority);
    }

    private bool TryGetUiDispatcher([MaybeNullWhen(false)] out Dispatcher dispatcher)
    {
        dispatcher = default;
        if (Window is null)
            return false;

        if (Window.Dispatcher is null || Window.Dispatcher.HasShutdownStarted)
            return false;

        dispatcher = Window.Dispatcher;
        return true;
    }

    #region Pending Relevance/Implementation

    public double LastAudioCallbackTime { get; set; }

    public void Stop()
    {
        WavePlayer.Close();
    }

    public void CloseAudioDevice()
    {
        throw new NotImplementedException();
    }

    public void HandleFatalException(Exception ex)
    {
        throw new NotImplementedException();
    }

    public AudioParams? OpenAudioDevice(AudioParams audioParams)
    {
        WavePlayer = new WavePlayer(this);
        return WavePlayer.AudioParams;
    }

    public void PauseAudioDevice()
    {
        WavePlayer?.Pause();
    }

    #endregion
}
