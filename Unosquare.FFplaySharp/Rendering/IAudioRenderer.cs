namespace Unosquare.FFplaySharp.Rendering
{
    using Unosquare.FFplaySharp.Primitives;

    public interface IAudioRenderer : IComponentRenderer
    {
        public double AudioCallbackTime { get; }

        public int Volume { get; }

        public int Open(AudioParams wantedSpec, out AudioParams audioDeviceSpec);

        public void Pause();

        public void UpdateVolume(int sign, double step);
    }
}
