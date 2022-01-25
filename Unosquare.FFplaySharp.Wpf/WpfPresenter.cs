using System.Windows.Media.Imaging;

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

    private unsafe void RenderPicture(FrameHolder frame)
    {
        var uiDispatcher = Application.Current.Dispatcher;
        
        var hasLockedBuffer = false;

        PictureParams? bitmapSize = default;
        uiDispatcher.Invoke(() =>
        {
            bitmapSize = _bitmap is not null ? PictureParams.FromBitmap(_bitmap) : null;
            if (bitmapSize is null || !bitmapSize.MatchesDimensions(WantedPictureSize))
            {
                _bitmap = WantedPictureSize.CreateBitmap();
                Window.targetImage.Source = _bitmap;
            }

            var timeout = new Duration(TimeSpan.FromMilliseconds(0));
            hasLockedBuffer = _bitmap!.TryLock(timeout);
            bitmapSize = PictureParams.FromBitmap(_bitmap);
        });

        if (!hasLockedBuffer)
            return;

        if (frame.Frame.PixelFormat != bitmapSize.PixelFormat)
        {
            var convert = Container.Video.ConvertContext;

            convert.Reallocate(frame.Width, frame.Height, frame.Frame.PixelFormat,
                bitmapSize!.Width, bitmapSize.Height, bitmapSize.PixelFormat);

            convert.Convert(frame.Frame.Data, frame.Frame.LineSize, frame.Frame.Height,
                bitmapSize.Buffer, bitmapSize.Stride);
        }
        else
        {
            var sourceLineSize = frame.Frame.LineSize[0];
            var targetLineSize = bitmapSize.Stride;
            var sz = Math.Min(sourceLineSize, targetLineSize);

            for (var lineIndex = 0; lineIndex < bitmapSize.Height; lineIndex++)
                Buffer.MemoryCopy(
                    frame.Frame.Data[0] + (lineIndex * sourceLineSize),
                    (void*)(bitmapSize.Buffer + (lineIndex * targetLineSize)),
                    sz,
                    sz);
        }

        frame.MarkUploaded();
        var updateRect = bitmapSize.ToRect();
        uiDispatcher.InvokeAsync(() =>
        {
            _bitmap.AddDirtyRect(updateRect);
            _bitmap.Unlock();
        });
    }

    private unsafe void PictureWorker()
    {
        while (Container.Video.Frames.IsClosed || WantedPictureSize is null)
            Thread.Sleep(1);

        double? videoStartTime = default;
        var frameStartTime = Clock.SystemTime;

        while (!Container.IsAtEndOfStream || Container.Video.Frames.HasPending)
        {
            var frame = Container.Video.Frames.WaitPeekShowable();
            if (frame is null) break;

            if (!frame.IsUploaded)
                RenderPicture(frame);

            var duration = frame.Duration;
            var elapsed = Clock.SystemTime - frameStartTime;
            
            if (elapsed < duration)
            {
                Thread.Sleep(1);
                continue;
            }

            Debug.WriteLine(
                $"Frame Received: RT: {(Clock.SystemTime - videoStartTime):n3} VCLK: {Container.VideoClock.Value:n3} FT: {frame.Time:n3}");

            if (frame.HasValidTime)
            {
                if (!videoStartTime.HasValue)
                    videoStartTime = Clock.SystemTime - frame.Time + frame.Duration;

                Container.UpdateVideoPts(frame.Time, frame.GroupIndex);
            }

            Container.Video.Frames.Dequeue();
            frameStartTime = Clock.SystemTime;
        }

        Debug.WriteLine(
            $"Done reading and displaying frames. RT: {(Clock.SystemTime - videoStartTime):n3} VCLK: {Container.VideoClock.Value:n3}");
    }

    public void Start()
    {
        var thread = new Thread(PictureWorker) { IsBackground = true };
        thread.Start();
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

    private record PictureParams()
    {
        public int Width { get; set; } = default;

        public int Height { get; set; } = default;

        public int DpiX { get; set; } = default;

        public int DpiY { get; set; } = default;

        public IntPtr Buffer { get; set; } = default;

        public int Stride { get; set; } = default;

        public AVPixelFormat PixelFormat { get; set; } = AVPixelFormat.AV_PIX_FMT_NONE;

        public WriteableBitmap CreateBitmap() =>
            new(Width, Height, DpiX, DpiY, System.Windows.Media.PixelFormats.Bgra32, null);

        public Int32Rect ToRect() => new(0, 0, Width, Height);

        public bool MatchesDimensions(PictureParams other) =>
            Width == other.Width && Height == other.Height && DpiX == other.DpiX && DpiY == other.DpiY;

        public static PictureParams FromBitmap(WriteableBitmap bitmap) => new()
        {
            Buffer = bitmap.BackBuffer,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            DpiX = Convert.ToInt32(bitmap.DpiX),
            DpiY = Convert.ToInt32(bitmap.DpiY),
            PixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA,
            Stride = bitmap.BackBufferStride
        };
    }
}

