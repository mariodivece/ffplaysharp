namespace Unosquare.FFplaySharp.Rendering;

public interface IAudioRenderer : IComponentRenderer
{
    public double AudioCallbackTime { get; }

    public int Volume { get; }

    public AudioParams Open(AudioParams wantedSpec);

    public void Pause();

    public void UpdateVolume(int sign, double step);
}
