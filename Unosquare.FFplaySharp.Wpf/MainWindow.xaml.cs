namespace Unosquare.FFplaySharp.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Initialized;
        }

        private void MainWindow_Initialized(object? sender, EventArgs e)
        {
            Helpers.SetFFmpegRootPath(@"C:\ffmpeg\x64");
            FFLog.Flags = ffmpeg.AV_LOG_SKIP_REPEATED;
            FFLog.Level = ffmpeg.AV_LOG_VERBOSE;

            // register all codecs, demux and protocols
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();

            var o = new ProgramOptions
            {
                InputFileName = @"C:\Users\UnoSp\OneDrive\ffme-testsuite\video-hevc-stress-01.mkv", //video-subtitles-03.mkv",
                IsAudioDisabled = true,
                IsSubtitleDisabled = true,
            };

            var presenter = new WpfPresenter() { Window = this };
            var container = MediaContainer.Open(o, presenter);
            presenter.Start();
        }
    }
}
