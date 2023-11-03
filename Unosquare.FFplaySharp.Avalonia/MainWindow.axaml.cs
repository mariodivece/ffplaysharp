using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading;
using Rendering = Avalonia.Rendering;

namespace Unosquare.FFplaySharp.Avalonia
{
    public partial class MainWindow : Window
    {
        private MediaContainer? Container;
        private long _HasLoaded;

        public MainWindow()
        {
            InitializeComponent();

            RendererDiagnostics.DebugOverlays |=
                Rendering.RendererDebugOverlays.Fps |
                Rendering.RendererDebugOverlays.RenderTimeGraph;
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (Interlocked.Increment(ref _HasLoaded) > 1)
                return;

            var options = (Application.Current as App)!.Options;

            if (string.IsNullOrWhiteSpace(options.InputFileName))
                return;

            //options.IsAudioDisabled = true;

            options.IsSubtitleDisabled = true;
            
            Container = MediaContainer.Open(options, AvaloniaPresenter);
            AvaloniaPresenter.Start();
        }

    }
}