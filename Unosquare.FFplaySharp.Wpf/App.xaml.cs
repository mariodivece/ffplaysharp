namespace Unosquare.FFplaySharp.Wpf;
using FFmpegBindings = FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        FFmpegBindings.LibrariesPath = @"C:\ffmpeg\x64";
        FFmpegBindings.Initialize();

        FFLog.Flags = ffmpeg.AV_LOG_SKIP_REPEATED;
        FFLog.Level = ffmpeg.AV_LOG_VERBOSE;

        // register all codecs, demux and protocols
        ffmpeg.avdevice_register_all();
        ffmpeg.avformat_network_init();
        Options = ProgramOptions.FromCommandLineArguments(e.Args);
        base.OnStartup(e);
    }

    public static ProgramOptions Options { get; private set; } = new();
}
