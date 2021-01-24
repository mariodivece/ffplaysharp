namespace Unosquare.FFplaySharp.Rendering
{
    using FFmpeg.AutoGen;
    using System.Collections.Generic;

    public interface IVideoRenderer : IComponentRenderer
    {
        public string WindowTitle { get; set; }

        public bool ForceRefresh { get; set; }

        public int screen_width { get; set; }

        public int screen_height { get; set; }

        public void ToggleFullScreen();

        public IEnumerable<AVPixelFormat> RetrieveSupportedPixelFormats();

        public void set_default_window_size(int width, int height, AVRational sar);

        public void Present(ref double remainingTime);
    }
}
