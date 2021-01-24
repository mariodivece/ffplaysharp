namespace Unosquare.FFplaySharp.Rendering
{
    using Unosquare.FFplaySharp.Primitives;

    public interface IAudioRenderer : IComponentRenderer
    {
        public double AudioCallbackTime { get; }

        public int audio_volume { get; set; }

        public int audio_open(AudioParams wantedSpec, out AudioParams audioDeviceSpec);

        public void CloseAudio();

        public void PauseAudio();

        public void update_volume(int sign, double step);
    }
}
