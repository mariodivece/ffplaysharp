namespace Unosquare.FFplaySharp.Wpf;


/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private long IsFirstRender = 1;
    private MediaContainer? Container;
    private IPresenter? Presenter;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (Interlocked.CompareExchange(ref IsFirstRender, 0, 1) == 0)
            return;

        if (string.IsNullOrWhiteSpace(App.Options.InputFileName))
            return;

        App.Options.IsAudioDisabled = true;
        App.Options.IsSubtitleDisabled = true;
        Presenter = new WpfPresenter() { Window = this };
        Container = MediaContainer.Open(App.Options, Presenter);
        Presenter.Start();

        var player = new WavePlayer();
        player.Start();
    }
}
