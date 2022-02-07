using System.Windows.Media.Imaging;

namespace Unosquare.FFplaySharp.Wpf;

internal record PictureParams()
{
    public int Width { get; set; } = default;

    public int Height { get; set; } = default;

    public int DpiX { get; set; } = default;

    public int DpiY { get; set; } = default;

    public IntPtr Buffer { get; set; } = default;

    public int Stride { get; set; } = default;

    public AVPixelFormat PixelFormat { get; set; } = AVPixelFormat.AV_PIX_FMT_NONE;

    public WriteableBitmap CreateBitmap() =>
        new(Width, Height, DpiX, DpiY, System.Windows.Media.PixelFormats.Bgra32, null);

    public Int32Rect ToRect() => new(0, 0, Width, Height);

    public bool MatchesDimensions(PictureParams other) =>
        Width == other.Width && Height == other.Height && DpiX == other.DpiX && DpiY == other.DpiY;

    public static PictureParams FromDimensions(int width, int height, AVRational sar)
    {
        var isValidSar = Math.Abs(sar.den) > 0 && Math.Abs(sar.num) > 0;

        return new()
        {
            Width = width,
            Height = height,
            DpiX = isValidSar ? sar.den : 96,
            DpiY = isValidSar ? sar.num : 96,
            PixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA,
        };
    }

    public static PictureParams FromBitmap(WriteableBitmap bitmap) => new()
    {
        Buffer = bitmap.BackBuffer,
        Width = bitmap.PixelWidth,
        Height = bitmap.PixelHeight,
        DpiX = Convert.ToInt32(bitmap.DpiX),
        DpiY = Convert.ToInt32(bitmap.DpiY),
        PixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA,
        Stride = bitmap.BackBufferStride
    };
}
