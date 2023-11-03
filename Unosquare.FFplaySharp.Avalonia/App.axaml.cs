using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFmpeg.AutoGen;
using FFmpeg;

namespace Unosquare.FFplaySharp.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                desktop.Startup += HandleStartup;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void HandleStartup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
        {
            Helpers.SetFFmpegRootPath(@"C:\ffmpeg\x64");
            FFLog.Flags = ffmpeg.AV_LOG_SKIP_REPEATED;
            FFLog.Level = ffmpeg.AV_LOG_VERBOSE;

            // register all codecs, demux and protocols
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();
            Options = ProgramOptions.FromCommandLineArguments(e.Args);
        }

        public ProgramOptions Options { get; private set; } = new();
    }
}