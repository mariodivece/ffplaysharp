using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Unosquare.FFplaySharp.Primitives;
using Unosquare.FFplaySharp.WinWave;
using Unosquare.Hpet;

namespace Unosquare.FFplaySharp.Avalonia.Views;

internal class MediaPresenter : VideoPresenterBase, IPresenter
{
    public MediaContainer Container { get; private set; }

    public double LastAudioCallbackTime { get; set; }

    public IReadOnlyList<AVPixelFormat> PixelFormats { get; } = new[] { AVPixelFormat.AV_PIX_FMT_BGRA };

    public void CloseAudioDevice() => throw new NotImplementedException();

    public void HandleFatalException(Exception ex) => throw new NotImplementedException();

    public bool Initialize(MediaContainer container)
    {
        Container = container;
        Container.Options.IsFrameDropEnabled = DropFrames ? ThreeState.On : ThreeState.Off;
        return true;
    }

    public AudioParams? OpenAudioDevice(AudioParams audioParams)
    {
        WavePlayer = new WavePlayer(this);
        return WavePlayer.AudioParams;
    }

    public void PauseAudioDevice() => WavePlayer?.Pause();

    public void Stop()
    {
        lock (SyncLock)
        {
            isRunning = false;
            WavePlayer?.Close();
        }
    }

    private WriteableBitmap? TargetBitmap;
    private WavePlayer? WavePlayer;

    private const double MediaSyncThresholdMin = 0.04;
    private const double MediaSyncThresholdMax = 0.01;
    private const double MediaSyncFrameDupThreshold = 0.01;
    private const bool DropFrames = true;

    private object SyncLock = new();
    private double remainingTime;
    private bool isRunning;
    private bool forceRefresh;
    private long droppedPictureCount;

    public void Start()
    {
        isRunning = true;
    }

    public void UpdatePictureSize(int width, int height, AVRational sar)
    {
        lock (SyncLock)
        {
            var requestedPictureSize = PictureParams.FromDimensions(width, height, new() { num = sar.num * 96, den = sar.den * 96 });
            var currentPictureSize = PictureParams.FromDimensions(TargetBitmap);
            if (TargetBitmap is not null && requestedPictureSize.MatchesDimensions(currentPictureSize))
                return;

            TargetBitmap?.Dispose();
            TargetBitmap = requestedPictureSize.ToWriteableBitmap();
        }
    }

    public override void Render(DrawingContext context)
    {
        try
        {
            lock (SyncLock)
            {
                if (Container is null)
                    return;

                Container.Options.VideoMaxPixelWidth = (int)(Bounds.Width * 96d / 72d);
                Container.Options.VideoMaxPixelHeight = (int)(Bounds.Height * 96d / 72d);

                if (Bounds.Width <= 0 || Bounds.Height <= 0 || !isRunning)
                    return;

                if (TargetBitmap is null || Container.Video.Frames.IsClosed)
                    return;

                if (WavePlayer is not null && !WavePlayer.HasStarted)
                    WavePlayer.Start();

                if (remainingTime > 0.001)
                    return;

                remainingTime = 0.001;
                PicturePixelSize = TargetBitmap?.PixelSize ?? default;

                UpdateContextRects();

                if (Container.ShowMode is not ShowMode.None && (!Container.IsPaused || forceRefresh))
                {
                    Present(context);
                    context.DrawImage(TargetBitmap!, ContextSourceRect, ContextTargetRect);
                }
            }
        }
        finally
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                InvalidateVisual();
            }, DispatcherPriority.Background);

        }

    }

    public void Present(DrawingContext context)
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
                        droppedPictureCount++;
                        Container.Video.Frames.Dequeue();
                        goto retry;
                    }
                }

                Container.Video.Frames.Dequeue();
                forceRefresh = true;

                if (Container.IsInStepMode && !Container.IsPaused)
                    Container.StreamTogglePause();
            }
        display:
            /* display picture */
            if (!Container.Options.IsDisplayDisabled &&
                forceRefresh &&
                Container.ShowMode == ShowMode.Video &&
                Container.Video.Frames.IsReadIndexShown)
            {
                RenderCurrentFrame();

            }
        }

        forceRefresh = false;
    }

    private unsafe bool RenderCurrentFrame()
    {
        var source = Container.Video.Frames.WaitPeekReadable();
        if (source is null) return false;

        try
        {
            if (TargetBitmap is null)
                return false;

            using var backBuffer = TargetBitmap.Lock();
            var target = new PictureParams
            {
                Buffer = backBuffer.Address,
                DpiX = (int)backBuffer.Dpi.X,
                DpiY = (int)backBuffer.Dpi.Y,
                Height = backBuffer.Size.Height,
                Width = backBuffer.Size.Width,
                PixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA,
                Stride = backBuffer.RowBytes
            };

            // When not using filtering, the software scaler will kick in.
            // I don't see when this would be true, but leaving in here just in case.
            if (source.Frame.PixelFormat != target.PixelFormat)
            {
                var converter = Container.Video.ConvertContext;

                converter.Reallocate(
                    source.Width, source.Height, source.Frame.PixelFormat,
                    target.Width, target.Height, target.PixelFormat);

                converter.Convert(
                    source.Frame.Data, source.Frame.LineSize, source.Frame.Height,
                    target.Buffer, target.Stride);

                return true;
            }

            var sourceData = new byte_ptrArray4() { [0] = source.Frame.Data[0] };
            var targetData = new byte_ptrArray4() { [0] = (byte*)target.Buffer };
            var sourceStride = new long_array4() { [0] = source.Frame.LineSize[0] };
            var targetStride = new long_array4() { [0] = target.Stride };

            // This is FFmpeg's way of copying pictures.
            // The av_image_copy_uc_from is slightly faster than
            // av_image_copy_to_buffer or av_image_copy alternatives.
            ffmpeg.av_image_copy_uc_from(
                ref targetData, targetStride,
                in sourceData, sourceStride,
                target.PixelFormat, target.Width, target.Height);

            return true;
        }
        finally
        {
            source.MarkUploaded();
        }

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

        Debug.WriteLine($"video: delay={pictureDuration,-8:n4} A-V={-clockDifference,-8:n4}.");

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

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        isRunning = false;
        base.OnDetachedFromLogicalTree(e);
        TargetBitmap?.Dispose();
        TargetBitmap = null;
    }
}
