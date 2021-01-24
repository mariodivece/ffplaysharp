namespace Unosquare.FFplaySharp.Rendering
{
    public interface IComponentRenderer
    {
        public MediaContainer Container { get; }

        public IPresenter Presenter { get; }

        public void Initialize(IPresenter presenter);
    }
}
