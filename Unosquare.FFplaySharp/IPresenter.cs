namespace Unosquare.FFplaySharp;

public interface IPresenter
{
    MediaContainer Container { get; }

    double LastAudioCallbackTime { get; }

    IReadOnlyList<AVPixelFormat> PixelFormats { get; }

    bool Initialize(MediaContainer container);

    void Start();

    void Stop();

    void UpdatePictureSize(int width, int height, AVRational sar);

    AudioParams? OpenAudioDevice(AudioParams audioParams);

    void PauseAudioDevice();

    void CloseAudioDevice();

    void HandleFatalException(Exception ex);
}
