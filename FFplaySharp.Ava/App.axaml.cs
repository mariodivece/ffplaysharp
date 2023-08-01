using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FFmpeg.AutoGen;
using FFplaySharp.Ava.ViewModels;
using FFplaySharp.Ava.Views;
using Unosquare.FFplaySharp;

namespace FFplaySharp.Ava;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);


        Helpers.SetFFmpegRootPath(@"c:\ffmpeg\x64");
        FFLog.Flags = ffmpeg.AV_LOG_SKIP_REPEATED;
        FFLog.Level = ffmpeg.AV_LOG_VERBOSE;

        // register all codecs, demux and protocols
        ffmpeg.avdevice_register_all();
        ffmpeg.avformat_network_init();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Options = ProgramOptions.FromCommandLineArguments(desktop.Args);
        }
    }

    public static ProgramOptions Options { get; private set; } = new();

    public override void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}