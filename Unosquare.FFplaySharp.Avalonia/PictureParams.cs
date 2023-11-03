using Avalonia;
using Avalonia.Media.Imaging;
using FFmpeg.AutoGen;
using System;

using Platform = Avalonia.Platform;

namespace Unosquare.FFplaySharp.Avalonia;


internal record struct PictureParams()
{
    public int Width { get; set; } = default;

    public int Height { get; set; } = default;

    public int DpiX { get; set; } = default;

    public int DpiY { get; set; } = default;

    public IntPtr Buffer { get; set; } = default;

    public int Stride { get; set; } = default;

    public AVPixelFormat PixelFormat { get; set; } = AVPixelFormat.AV_PIX_FMT_NONE;

    public PixelSize PixelSize => new(Width, Height);

    public bool MatchesDimensions(PictureParams other) =>
        Width == other.Width && Height == other.Height && DpiX == other.DpiX && DpiY == other.DpiY;

    public WriteableBitmap ToWriteableBitmap() => new(
        PixelSize, new(DpiX, DpiY), Platform.PixelFormat.Bgra8888, Platform.AlphaFormat.Unpremul);

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

    public static PictureParams FromDimensions(Bitmap? bitmap)
    {
        if (bitmap is null)
            return new();

        var isValidSar = Math.Abs(bitmap.Dpi.X) > 0 && Math.Abs(bitmap.Dpi.Y) > 0;

        return new()
        {
            Width = bitmap.PixelSize.Width,
            Height = bitmap.PixelSize.Height,
            DpiX = isValidSar ? (int)bitmap.Dpi.X : 96,
            DpiY = isValidSar ? (int)bitmap.Dpi.Y : 96,
            PixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA,
        };
    }


}

