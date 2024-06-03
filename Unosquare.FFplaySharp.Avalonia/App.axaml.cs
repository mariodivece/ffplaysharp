using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFmpegBindings = FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings;
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
            FFmpegBindings.LibrariesPath = @"C:\ffmpeg\x64";
            FFmpegBindings.Initialize();
            
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