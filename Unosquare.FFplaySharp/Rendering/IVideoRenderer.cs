namespace Unosquare.FFplaySharp.Rendering
{
    using FFmpeg.AutoGen;
    using System.Collections.Generic;

    public interface IVideoRenderer : IComponentRenderer
    {
        public string window_title { get; set; }

        public bool force_refresh { get; set; }

        public int screen_width { get; set; }

        public int screen_height { get; set; }

        public void toggle_full_screen();

        public IEnumerable<AVPixelFormat> RetrieveSupportedPixelFormats();

        public void set_default_window_size(int width, int height, AVRational sar);

        public void video_refresh(MediaContainer container, ref double remainingTime);

        public void CloseVideo();
    }
}
