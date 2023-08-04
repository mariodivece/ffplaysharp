using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Unosquare.FFplaySharp;

namespace FFplaySharp.Ava.Views;

public partial class MainWindow : Window
{
    private long IsFirstRender = 1;
    private MediaContainer? Container;
    private IPresenter? Presenter;
    public MainWindow()
    {
        InitializeComponent();

        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref IsFirstRender, 0, 1) == 0)
            return;

        if (string.IsNullOrWhiteSpace(App.Options.InputFileName))
            return;

        //App.Options.IsAudioDisabled = true;
        App.Options.IsSubtitleDisabled = true;
        Presenter = new AvaPresenter() { Window = this };
        Container = MediaContainer.Open(App.Options, Presenter);
        Presenter.Start();
    }
}