﻿using System.Windows.Media.Imaging;

namespace Unosquare.FFplaySharp.Wpf;

internal class WpfPresenter : IPresenter
{
    private PictureParams? WantedPictureSize = default;

    private WriteableBitmap _bitmap;

    public MainWindow Window { get; init; }

    public MediaContainer Container { get; private set; }

    public double LastAudioCallbackTime => Clock.SystemTime;

    public IReadOnlyList<AVPixelFormat> PixelFormats { get; } = new[] { AVPixelFormat.AV_PIX_FMT_BGRA };

    public void CloseAudioDevice()
    {
        throw new NotImplementedException();
    }

    public void HandleFatalException(Exception ex)
    {
        throw new NotImplementedException();
    }

    public bool Initialize(MediaContainer container)
    {
        Container = container;
        return true;
    }

    public AudioParams? OpenAudioDevice(AudioParams audioParams)
    {
        throw new NotImplementedException();
    }

    public void PauseAudioDevice()
    {
        throw new NotImplementedException();
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

    const bool UseNativeMethod = false;
    private readonly Stopwatch sw = new();
    private readonly List<double> samples = new(4096 * 2);

    private unsafe void CopyPicture(FrameHolder source, PictureParams target, bool useNativeMethod)
    {
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

    private unsafe void RenderPicture(FrameHolder frame)
    {
        if (!TryGetUiDispatcher(out var uiDispatcher))
            return;

        var hasLockedBuffer = false;
        PictureParams? bitmapSize = default;

        uiDispatcher.Invoke(() =>
        {
            //Debug.WriteLine($"UI Thread: {Environment.CurrentManagedThreadId}");
            bitmapSize = _bitmap is not null ? PictureParams.FromBitmap(_bitmap) : null;
            if (bitmapSize is null || !bitmapSize.MatchesDimensions(WantedPictureSize!))
            {
                _bitmap = WantedPictureSize!.CreateBitmap();
                Window.targetImage.Source = _bitmap;
            }
            
            var timeout = new Duration(TimeSpan.FromMilliseconds(0));
            hasLockedBuffer = _bitmap!.TryLock(timeout);
            bitmapSize = PictureParams.FromBitmap(_bitmap);
        });

        if (!hasLockedBuffer)
            return;


        if (frame.Frame.PixelFormat != bitmapSize!.PixelFormat)
        {
            var convert = Container.Video.ConvertContext;

            convert.Reallocate(frame.Width, frame.Height, frame.Frame.PixelFormat,
                bitmapSize!.Width, bitmapSize.Height, bitmapSize.PixelFormat);

            convert.Convert(frame.Frame.Data, frame.Frame.LineSize, frame.Frame.Height,
                bitmapSize.Buffer, bitmapSize.Stride);
        }
        else
        {
            sw.Restart();
            CopyPicture(frame, bitmapSize, UseNativeMethod);
            samples.Add(sw.ElapsedTicks);
        }

        frame.MarkUploaded();
        var updateRect = bitmapSize.ToRect();
        uiDispatcher.Invoke(() =>
        {
            _bitmap.AddDirtyRect(updateRect);
            _bitmap.Unlock();
        });
    }

    private MultimediaTimer RenderTimer;

    public void Start()
    {
        double? videoStartTime = default;
        var frameStartTime = Clock.SystemTime;

        RenderTimer = new(1, 1);
        RenderTimer.Elapsed += (s, e) =>
        {
            if (Container.Video.Frames.IsClosed || WantedPictureSize is null)
                return;

            if (Container.IsAtEndOfStream && !Container.Video.Frames.HasPending)
                Debug.WriteLine(
                    $"Done reading and displaying frames. RT: {(Clock.SystemTime - videoStartTime):n3} VCLK: {Container.VideoClock.Value:n3}");

            //Debug.WriteLine($"Timer Thread: {Environment.CurrentManagedThreadId}");

            var frame = Container.Video.Frames.WaitPeekShowable();
            if (frame is null) return;

            if (!frame.IsUploaded)
                RenderPicture(frame);

            var duration = frame.Duration;
            var elapsed = Clock.SystemTime - frameStartTime;

            if (elapsed < duration)
                return;

            //Debug.WriteLine(
            //    $"Frame Received: RT: {(Clock.SystemTime - videoStartTime):n3} VCLK: {Container.VideoClock.Value:n3} FT: {frame.Time:n3}");

            if (frame.HasValidTime)
            {
                if (!videoStartTime.HasValue)
                    videoStartTime = Clock.SystemTime - frame.Time + frame.Duration;

                Container.UpdateVideoPts(frame.Time, frame.GroupIndex);
            }

            Container.Video.Frames.Dequeue();
            frameStartTime = Clock.SystemTime;
        };
        RenderTimer.Start();
        //ThreadPool.QueueUserWorkItem((s) => RenderTimer.Start());
        //RenderTimer.Start();
    }

    public void Stop()
    {
        throw new NotImplementedException();
    }

    public void UpdatePictureSize(int width, int height, AVRational sar)
    {
        if (WantedPictureSize is null)
            WantedPictureSize = new();

        var isValidSar = Math.Abs(sar.den) > 0 && Math.Abs(sar.num) > 0;

        WantedPictureSize.Width = width;
        WantedPictureSize.Height = height;
        WantedPictureSize.DpiX = isValidSar ? sar.den : 96;
        WantedPictureSize.DpiY = isValidSar ? sar.num : 96;
        WantedPictureSize.PixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA;
    }
}
