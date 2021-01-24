namespace Unosquare.FFplaySharp.Rendering
{
    public interface IPresenter
    {
        public IVideoRenderer Video { get; }

        public IAudioRenderer Audio { get; }

        public MediaContainer Container { get; }

        public bool Initialize(MediaContainer container);

        public void Start();

        public void Stop();
    }
}
