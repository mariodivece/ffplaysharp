using System.Globalization;
using System.Text;
using System.Windows.Media;
using Unosquare.Hpet;

namespace Unosquare.FFplaySharp.Wpf;

internal class WpfPresenter : IPresenter
{
    const double MediaSyncThresholdMin = 0.04;
    const double MediaSyncThresholdMax = 0.01;
    const double MediaSyncFrameDupThreshold = 0.01;

    private const bool UseNativeMethod = true;
    private const bool DropFrames = true;

    private static readonly Duration LockTimeout = new(TimeSpan.FromMilliseconds(0));
    private readonly PrecisionTimer RenderTimer = new(TimeSpan.FromMilliseconds(1), DelayPrecision.Default);
    private PictureParams CurrentPictureSize = new();
    private PictureParams? RequestedPictureSize;
    private WriteableBitmap? TargetBitmap;
    private WavePlayer WavePlayer;

    private bool ForceRefresh;
    private int DroppedPictureCount;

    public MainWindow? Window { get; init; }

    public MediaContainer Container { get; private set; }

    public IReadOnlyList<AVPixelFormat> PixelFormats { get; } = new[] { AVPixelFormat.AV_PIX_FMT_BGRA };

    public bool Initialize(MediaContainer container)
    {
        Container = container;
        return true;
    }

    public void Start()
    {
        if (Window is null)
            throw new InvalidOperationException("Cannot proceed with rendering without setting the Window property");

        var remainingTime = 0d;

        RenderTimer.Ticked += (s, e) =>
        {
            if (CurrentPictureSize is null || Container.Video.Frames.IsClosed)
                return;

            if (WavePlayer is not null && !WavePlayer.HasStarted)
                WavePlayer.Start();

            if (remainingTime > 0.001)
                return;

            remainingTime = 0.001;

            if (Container.ShowMode is not ShowMode.None && (!Container.IsPaused || ForceRefresh))
                Present(ref remainingTime);
        };

        RenderTimer.Start();
    }

    static TimeExtent ComputePictureDisplayDuration(TimeExtent pictureDuration, MediaContainer container)
    {
        var clockDifference = TimeExtent.Zero;

        /* update delay to follow master synchronisation source */
        if (container.MasterSyncMode != ClockSource.Video)
        {
            /* if video is slave, we try to correct big delays by
               duplicating or deleting a frame */
            clockDifference = container.VideoClock.Value - container.MasterTime;

            /* skip or repeat frame. We take into account the
               delay to compute the threshold. I still don't know
               if it is the best guess */
            var syncThreshold = Math.Max(MediaSyncThresholdMin, Math.Min(MediaSyncThresholdMax, pictureDuration));
            if (!clockDifference.IsNaN && Math.Abs(clockDifference) < container.MaxPictureDuration)
            {
                if (clockDifference <= -syncThreshold)
                    pictureDuration = Math.Max(0, pictureDuration + clockDifference);
                else if (clockDifference >= syncThreshold && pictureDuration > MediaSyncFrameDupThreshold)
                    pictureDuration += clockDifference;
                else if (clockDifference >= syncThreshold)
                    pictureDuration = 2.0 * pictureDuration;
            }
        }

        ($"video: delay={pictureDuration,-8:n4} A-V={-clockDifference,-8:n4}.").LogTrace();

        return pictureDuration;
    }

    static TimeExtent ComputePictureDuration(MediaContainer container, FrameHolder currentFrame, FrameHolder nextFrame)
    {
        if (currentFrame.GroupIndex != nextFrame.GroupIndex)
            return 0.0;

        var pictureDuration = nextFrame.Time - currentFrame.Time;
        if (pictureDuration.IsNaN || pictureDuration <= 0 || pictureDuration > container.MaxPictureDuration)
            return currentFrame.Duration;
        else
            return pictureDuration;
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

    private unsafe bool RenderToBackBuffer()
    {
        var frame = Container.Video.Frames.WaitPeekReadable();

        if (frame is null)
            return false;
        

        UiInvoke(() =>
        {
            UiRecreatePictureIfRequired();

            if (TargetBitmap is null)
                return;

            TargetBitmap.Lock();
            CurrentPictureSize.Buffer = TargetBitmap.BackBuffer;
            CurrentPictureSize.Stride = TargetBitmap.BackBufferStride;
            CopyPictureFrame(Container, frame, CurrentPictureSize, UseNativeMethod);
            TargetBitmap.AddDirtyRect(CurrentPictureSize.ToRect());
            TargetBitmap.Unlock();

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

    /* called to display each frame */
    public void Present(ref double remainingTime)
    {
        if (!Container.IsPaused && Container.MasterSyncMode == ClockSource.External && Container.IsRealTime)
            Container.SyncExternalClockSpeed();

        if (Container.HasVideo)
        {
        retry:
            if (!Container.Video.Frames.HasPending)
            {
                // nothing to do, no picture to display in the queue
            }
            else
            {
                /* dequeue the picture */
                var previousPicture = Container.Video.Frames.WaitPeekReadable();
                var currentPicture = Container.Video.Frames.PeekShowable();

                if (currentPicture.GroupIndex != Container.Video.Packets.GroupIndex)
                {
                    Container.Video.Frames.Dequeue();
                    goto retry;
                }

                if (previousPicture.GroupIndex != currentPicture.GroupIndex)
                    Container.PictureDisplayTimer = Clock.SystemTime;

                if (Container.IsPaused)
                    goto display;

                // compute nominal last_duration
                var pictureDuration = ComputePictureDuration(Container, previousPicture, currentPicture);
                var pictureDisplayDuration = ComputePictureDisplayDuration(pictureDuration, Container);

                var currentTime = Clock.SystemTime;
                if (currentTime < Container.PictureDisplayTimer + pictureDisplayDuration)
                {
                    remainingTime = Math.Min(Container.PictureDisplayTimer + pictureDisplayDuration - currentTime, remainingTime);
                    goto display;
                }

                Container.PictureDisplayTimer += pictureDisplayDuration;
                if (pictureDisplayDuration > 0 && currentTime - Container.PictureDisplayTimer > MediaSyncThresholdMax)
                    Container.PictureDisplayTimer = currentTime;

                if (currentPicture.HasValidTime)
                    Container.UpdateVideoPts(currentPicture.Time, currentPicture.GroupIndex);

                if (Container.Video.Frames.PendingCount > 1)
                {
                    var nextPicture = Container.Video.Frames.PeekShowablePlus();
                    var duration = ComputePictureDuration(Container, currentPicture, nextPicture);
                    if (Container.IsInStepMode == false &&
                        (Container.Options.IsFrameDropEnabled > 0 ||
                        (Container.Options.IsFrameDropEnabled != 0 && Container.MasterSyncMode != ClockSource.Video)) &&
                        currentTime > Container.PictureDisplayTimer + duration)
                    {
                        DroppedPictureCount++;
                        Container.Video.Frames.Dequeue();
                        goto retry;
                    }
                }

                Container.Video.Frames.Dequeue();
                ForceRefresh = true;

                if (Container.IsInStepMode && !Container.IsPaused)
                    Container.StreamTogglePause();
            }
        display:
            /* display picture */
            if (!Container.Options.IsDisplayDisabled && ForceRefresh && Container.ShowMode == ShowMode.Video && Container.Video.Frames.IsReadIndexShown)
                RenderToBackBuffer();
        }

        ForceRefresh = false;
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
