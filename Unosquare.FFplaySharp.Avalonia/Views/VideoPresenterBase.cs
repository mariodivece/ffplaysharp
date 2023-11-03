using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Unosquare.FFplaySharp.Avalonia.Views;

public abstract class VideoPresenterBase : Control
{
    /// <summary>
    /// Defines the <see cref="Stretch"/> property.
    /// </summary>
    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<VideoPresenterBase, Stretch>(nameof(Stretch), Stretch.Uniform);

    /// <summary>
    /// Defines the <see cref="StretchDirection"/> property.
    /// </summary>
    public static readonly StyledProperty<StretchDirection> StretchDirectionProperty =
        AvaloniaProperty.Register<VideoPresenterBase, StretchDirection>(
            nameof(StretchDirection),
            StretchDirection.Both);

    public static readonly StyledProperty<BitmapInterpolationMode> RenderInterpolationModeProperty =
        AvaloniaProperty.Register<VideoPresenterBase, BitmapInterpolationMode>(
            nameof(RenderInterpolationMode),
            BitmapInterpolationMode.None);

    public static readonly StyledProperty<EdgeMode> RenderEdgedModeProperty =
        AvaloniaProperty.Register<VideoPresenterBase, EdgeMode>(
            nameof(RenderEdgeMode),
            EdgeMode.Aliased);

    public static readonly StyledProperty<int> PicturePixelWidthProperty =
        AvaloniaProperty.Register<VideoPresenterBase, int>(nameof(PicturePixelWidth), 2560);

    public static readonly StyledProperty<int> PicturePixelHeightProperty =
        AvaloniaProperty.Register<VideoPresenterBase, int>(nameof(PicturePixelHeight), 1440);

    static VideoPresenterBase()
    {
        AffectsRender<VideoPresenterBase>(
            StretchProperty,
            StretchDirectionProperty,
            RenderInterpolationModeProperty,
            RenderEdgedModeProperty,
            PicturePixelWidthProperty,
            PicturePixelHeightProperty);

        AffectsMeasure<VideoPresenterBase>(
            StretchProperty,
            StretchDirectionProperty,
            PicturePixelWidthProperty,
            PicturePixelHeightProperty);

        AutomationProperties.ControlTypeOverrideProperty.OverrideDefaultValue<VideoPresenterBase>(
            AutomationControlType.Image);
    }

    /// <summary>
    /// Gets or sets a value controlling how the video will be stretched.
    /// </summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets a value controlling in what direction the video will be stretched.
    /// </summary>
    public StretchDirection StretchDirection
    {
        get => GetValue(StretchDirectionProperty);
        set => SetValue(StretchDirectionProperty, value);
    }

    public BitmapInterpolationMode RenderInterpolationMode
    {
        get => GetValue(RenderInterpolationModeProperty);
        set => SetValue(RenderInterpolationModeProperty, value);
    }

    public EdgeMode RenderEdgeMode
    {
        get => GetValue(RenderEdgedModeProperty);
        set => SetValue(RenderEdgedModeProperty, value);
    }

    public int PicturePixelWidth
    {
        get => GetValue(PicturePixelWidthProperty);
        set => SetValue(PicturePixelWidthProperty, value);
    }

    public int PicturePixelHeight
    {
        get => GetValue(PicturePixelHeightProperty);
        set => SetValue(PicturePixelHeightProperty, value);
    }

    protected static Vector PictureDpi { get; } = new(96, 96);

    protected PixelSize PicturePixelSize { get; set; }

    protected PixelFormat PicturePixelFormat { get; } = PixelFormats.Bgra8888;

    protected AlphaFormat PictureAlphaFormat { get; } = AlphaFormat.Unpremul;

    protected Rect ContextBoundsRect { get; set; }

    protected Rect ContextSourceRect { get; set; }

    protected Rect ContextTargetRect { get; set; }

    /// <inheritdoc />
    protected override bool BypassFlowDirectionPolicies => true;

    /// <summary>
    /// Measures the control.
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The desired size of the control.</returns>
    protected override Size MeasureOverride(Size availableSize) => PicturePixelWidth > 0 && PicturePixelHeight > 0
        ? Stretch.CalculateSize(availableSize, PicturePixelSize.ToSizeWithDpi(PictureDpi), StretchDirection)
        : default;

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize) => PicturePixelWidth > 0 && PicturePixelHeight > 0
        ? Stretch.CalculateSize(finalSize, PicturePixelSize.ToSizeWithDpi(PictureDpi))
        : default;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, RenderInterpolationMode);
        RenderOptions.SetEdgeMode(this, RenderEdgeMode);
        PicturePixelSize = new(PicturePixelWidth, PicturePixelHeight);
        base.OnInitialized();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        try
        {
            if (change.Property is null)
                return;

            if (change.Property == RenderInterpolationModeProperty && change.NewValue is BitmapInterpolationMode interpolationMode)
                RenderOptions.SetBitmapInterpolationMode(this, interpolationMode);

            if (change.Property == RenderEdgedModeProperty && change.NewValue is EdgeMode edgeMode)
                RenderOptions.SetEdgeMode(this, edgeMode);

            if (change.Property == PicturePixelWidthProperty || change.Property == PicturePixelHeightProperty)
                PicturePixelSize = new(PicturePixelWidth, PicturePixelHeight);
        }
        finally
        {
            base.OnPropertyChanged(change);
        }
    }

    protected virtual void UpdateContextRects()
    {
        ContextBoundsRect = new(0, 0, Bounds.Width, Bounds.Height);
        var boundsSize = Bounds.Size;
        var viewPort = new Rect(boundsSize);
        var dpiSize = PicturePixelSize.ToSizeWithDpi(PictureDpi);
        var scale = Stretch.CalculateScaling(boundsSize, dpiSize, StretchDirection);
        var scaledSize = dpiSize * scale;

        ContextTargetRect = viewPort
            .CenterRect(new(scaledSize))
            .Intersect(viewPort);

        ContextSourceRect = new Rect(dpiSize)
            .CenterRect(new(ContextTargetRect.Size / scale));
    }
}
