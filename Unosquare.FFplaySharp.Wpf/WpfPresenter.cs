namespace Unosquare.FFplaySharp.Wpf;

internal class WpfPresenter : IPresenter
{
    private const bool UseNativeMethod = false;
    private static readonly Duration LockTimeout = new(TimeSpan.FromMilliseconds(0));
    private readonly MultimediaTimer RenderTimer = new(1);

    private PictureParams CurrentPicture = new();
    private WriteableBitmap? TargetBitmap;
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
        // var samples = new List<double>(4096);
        var startNextFrame = true;
        var runtimeStopwatch = new Stopwatch();
        var frameStartTime = Clock.SystemTime;
        var frameDuration = default(double);
        var compensation = default(double);

        RenderTimer.Elapsed += (s, e) =>
        {
            if (CurrentPicture is null || Container.Video.Frames.IsClosed)
                return;

            if (Container.IsAtEndOfStream && !Container.Video.Frames.HasPending)
            {
                var totalRuntime = runtimeStopwatch.Elapsed.TotalSeconds + frameDuration;
                Debug.WriteLine($"Done reading and displaying frames. "
                    + $"RT: {totalRuntime:n3} VCLK: {Container.VideoClock.Value:n3}");

                UiInvoke(() =>
                {
                    if (!HasLockedBuffer)
                        return;

                    TargetBitmap?.AddDirtyRect(new(0, 0, TargetBitmap.PixelWidth, TargetBitmap.PixelHeight));
                    TargetBitmap?.Unlock();
                });

                RenderTimer.Dispose();
            }

            if (!Container.Video.Frames.HasPending)
                return;

            var frame = Container.Video.Frames.PeekShowable();
            if (frame is null) return;

            if (!runtimeStopwatch.IsRunning)
                runtimeStopwatch.Start();

            if (startNextFrame)
            {
                startNextFrame = false;
                compensation = frameDuration <= double.Epsilon
                    ? default : Clock.SystemTime - frameStartTime - frameDuration;

                // TODO: Perform cumulative compensation.
                // i.e. What happens if compensation is greater
                // than the new frame time or say, the following 2 frame times?
                // We would certainly need to skip the frames to keep timing optimal.
                frameStartTime = Clock.SystemTime;
                frameDuration = frame.Duration - compensation;
                if (frameDuration <= double.Epsilon)
                {
                    Container.Video.Frames.Dequeue();
                    startNextFrame = true;
                    return;
                }

                // samples.Add(compensation * 1000);
                // Debug.WriteLine($"Compensation: {compensation * 1000:n4}");
            }

            if (!frame.IsUploaded && !RenderBackBuffer(frame))
                return;

            if (Clock.SystemTime - frameStartTime < frameDuration)
                return;

            if (frame.HasValidTime)
                Container.UpdateVideoPts(frame.Time, frame.GroupIndex);

            Container.Video.Frames.Dequeue();
            startNextFrame = true;
        };
        RenderTimer.Start();
        //ThreadPool.QueueUserWorkItem((s) => RenderTimer.Start());
        //RenderTimer.Start();
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
                ref sourceData, sourceStride,
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

        ui?.Invoke(action, priority);
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

    public double LastAudioCallbackTime => Clock.SystemTime;

    public void Stop()
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    public void PauseAudioDevice()
    {
        throw new NotImplementedException();
    }

    #endregion
}
