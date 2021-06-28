namespace Unosquare.FFplaySharp.Rendering
{
    using Unosquare.FFplaySharp.Primitives;

    public interface IAudioRenderer : IComponentRenderer
    {
        public double AudioCallbackTime { get; }

        public int Volume { get; }

        public AudioParams Open(AudioParams wantedSpec);

        public void Pause();

        public void UpdateVolume(int sign, double step);
    }
}
