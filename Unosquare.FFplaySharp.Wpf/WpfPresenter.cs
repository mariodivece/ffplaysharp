using System.Windows.Controls;
using System.Windows.Media;
using Unosquare.Hpet;

namespace Unosquare.FFplaySharp.Wpf;

internal class WpfPresenter : IPresenter
{
    private const bool UseNativeMethod = true;
    private const bool DropFrames = true;

    private readonly object SyncLock = new();

    private static readonly Duration LockTimeout = new(TimeSpan.FromMilliseconds(0));
    private readonly PrecisionTimer RenderTimer = new(TimeSpan.FromMilliseconds(1), DelayPrecision.Default);
    private PictureParams CurrentPictureSize = new();
    private PictureParams? RequestedPictureSize;
    private WriteableBitmap? TargetBitmap;
    private WavePlayer WavePlayer;
    private long m_HasLockedBuffer = 0;
    private TimeSpan? LastRenderCallTime;


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
        if (Window is null)
            throw new InvalidOperationException("Cannot proceed with rendering without setting the Window property");

        CompositionTarget.Rendering += CompositionTarget_Rendering;

        var frameNumber = 0;
        var startNextFrame = default(bool);
        var runtimeStopwatch = new MultimediaStopwatch();
        var frameStopwatch = new MultimediaStopwatch();
        var pictureDuration = TimeExtent.NaN;
        var compensation = TimeExtent.Zero;
        var previousElapsed = TimeExtent.Zero;
        var elapsedSamples = new List<TimeExtent>(2048);

        RenderTimer.Ticked += (s, e) =>
        {
            if (CurrentPictureSize is null || Container.Video.Frames.IsClosed)
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

                Debug.WriteLine($"Frame Average Elapsed: {elapsedSamples.Average(c => c.Milliseconds):n4} ms.");

                RenderTimer.Dispose();
            }

        retry:
            if (!Container.Video.Frames.HasPending)
                return;

            var frame = Container.Video.Frames.WaitPeekShowable();
            if (frame is null)
                return;

            // Handle first incoming frame.
            if (pictureDuration.IsNaN)
            {
                runtimeStopwatch.Restart();
                frameStopwatch.Restart();
                pictureDuration = frame.Duration;
                startNextFrame = false;
            }

            if (startNextFrame)
            {
                startNextFrame = false;
                compensation = pictureDuration - previousElapsed;
                pictureDuration = frame.Duration + compensation;
                if (frame.HasValidTime)
                    Container.UpdateVideoPts(frame.Time, frame.GroupIndex);

                frameNumber++;
#if DEBUG
                Debug.WriteLine($"NUM: {frameNumber,-6} PREV: {previousElapsed.Milliseconds,6:n2} COMP: {compensation.Milliseconds,6:n2} NEXT: {pictureDuration.Milliseconds,6:n2}");
#endif
            }

            if (!frame.IsUploaded && (!DropFrames || pictureDuration > 0))
                RenderBackBuffer(frame);

            if (frameStopwatch.ElapsedSeconds >= pictureDuration)
            {
                Container.Video.Frames.Dequeue();
                startNextFrame = true;
                previousElapsed = frameStopwatch.Restart();
                elapsedSamples.Add(previousElapsed);
                goto retry;
            }
        };

        RenderTimer.Start();
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs renderingEvent)
            return;

        if (sender is not Dispatcher dispatcher)
            return;

        if (dispatcher != Window?.Dispatcher)
            return;

        if (LastRenderCallTime is not null && renderingEvent.RenderingTime == LastRenderCallTime)
            return;

        LastRenderCallTime = renderingEvent.RenderingTime;

    }

    public void UpdatePictureSize(int width, int height, AVRational sar)
    {
        var isFirstCall = RequestedPictureSize is null;
        RequestedPictureSize = PictureParams.FromDimensions(width, height, sar);

        if (!isFirstCall)
            return;

        UiInvoke(UiRecreatePictureIfRequired);
    }

    public void UiRecreatePictureIfRequired()
    {
        if (RequestedPictureSize is null ||
            CurrentPictureSize.MatchesDimensions(RequestedPictureSize))
            return;

        CurrentPictureSize = RequestedPictureSize;
        TargetBitmap = CurrentPictureSize.CreateBitmap();
        RenderOptions.SetBitmapScalingMode(Window!.targetImage, BitmapScalingMode.LowQuality);
        RenderOptions.SetEdgeMode(Window!.targetImage, EdgeMode.Aliased);
        Window.targetImage.SnapsToDevicePixels = true;
        Window!.targetImage.Source = TargetBitmap;
    }

    private unsafe bool RenderBackBuffer(FrameHolder frame)
    {
        if (TargetBitmap is null || HasLockedBuffer)
            return false;

        UiInvoke(() =>
        {
            UiRecreatePictureIfRequired();

            if (HasLockedBuffer)
                return;

            if (!TargetBitmap.TryLock(LockTimeout))
                return;

            CurrentPictureSize.Buffer = TargetBitmap.BackBuffer;
            CurrentPictureSize.Stride = TargetBitmap.BackBufferStride;
            HasLockedBuffer = true;
        }, DispatcherPriority.Loaded);

        lock (SyncLock)
        {
            if (!HasLockedBuffer)
                return false;

            CopyPictureFrame(Container, frame, CurrentPictureSize, UseNativeMethod);
        }


        UiInvokeAsync(() =>
        {
            if (!HasLockedBuffer)
                return;

            TargetBitmap.AddDirtyRect(CurrentPictureSize.ToRect());
            TargetBitmap.Unlock();
            HasLockedBuffer = false;
        }, DispatcherPriority.Render);

        return true;
    }

    private static unsafe void CopyPictureFrame(MediaContainer container, FrameHolder source, PictureParams target, bool useNativeMethod)
    {
        try
        {
            // When not using filtering, the software scaler will kick in.
            // I don't see when this would be true, but leaving in here just in case.
            if (source.Frame.PixelFormat != target.PixelFormat)
            {
                var converter = container.Video.ConvertContext;

                converter.Reallocate(
                    source.Width, source.Height, source.Frame.PixelFormat,
                    target.Width, target.Height, target.PixelFormat);

                converter.Convert(
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
        finally
        {
            source.MarkUploaded();
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
