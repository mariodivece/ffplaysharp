using System.Windows.Media.Imaging;

namespace Unosquare.FFplaySharp.Wpf;

internal class WpfPresenter : IPresenter
{
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

    public void Start()
    {
        var thread = new Thread((s) =>
        {
            while (Container.Video.Frames.IsClosed)
                Thread.Sleep(1);

            var convert = Container.Video.ConvertContext;

            while (!Container.IsAtEndOfStream || Container.Video.Frames.HasPending)
            {
                var frame = Container.Video.Frames.WaitPeekShowable();
                if (frame is null)
                    break;

                if (frame.HasValidTime)
                    Container.UpdateVideoPts(frame.Time, frame.GroupIndex);

                var duration = frame.Duration;
                Debug.WriteLine($"Frame Received: {Container.VideoClock.Value}");

                if (_bitmap is not null)
                {
                    IntPtr buffer = IntPtr.Zero;
                    int stride = default;
                    var pixelWidth = 0;
                    var pixelHeight = 0;
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        pixelWidth = _bitmap.PixelWidth;
                        pixelHeight = _bitmap.PixelHeight;
                        _bitmap.Lock();
                        buffer = _bitmap.BackBuffer;
                        stride = _bitmap.BackBufferStride;
                    });

                    convert.Reallocate(frame.Width, frame.Height, frame.Frame.PixelFormat,
                        pixelWidth, pixelHeight, PixelFormats[0]);

                    unsafe
                    {
                        convert.Convert(frame.Frame.Data, frame.Frame.LineSize, pixelHeight, buffer, stride);
                    }

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        _bitmap.AddDirtyRect(new(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight));
                        _bitmap.Unlock();
                    });
                }


                Container.Video.Frames.Dequeue();
                Thread.Sleep(TimeSpan.FromSeconds(duration));
            }

            Debug.WriteLine("Doine reading frames");
        })
        {
            IsBackground = true
        };

        thread.Start();
    }

    public void Stop()
    {
        throw new NotImplementedException();
    }

    public void UpdatePictureSize(int width, int height, AVRational sar)
    {

        if (_bitmap is not null)
            return;

        App.Current.Dispatcher.Invoke(() =>
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            Window.targetImage.Source = _bitmap;
        });

    }
}

