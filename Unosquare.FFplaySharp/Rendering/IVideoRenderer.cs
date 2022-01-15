namespace Unosquare.FFplaySharp.Rendering;

public interface IVideoRenderer : IComponentRenderer
{
    public string WindowTitle { get; set; }

    public bool ForceRefresh { get; set; }

    public int ScreenWidth { get; set; }

    public int ScreenHeight { get; set; }

    public void ToggleFullScreen();

    public IEnumerable<AVPixelFormat> RetrieveSupportedPixelFormats();

    public void SetDefaultWindowSize(int width, int height, AVRational sar);

    public void Present(ref double remainingTime);
}

