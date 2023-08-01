using Avalonia;
using Avalonia.Media.Imaging;
using FFmpeg.AutoGen;
using System;
using Avalonia.Platform;

namespace FFplaySharp.Ava
{
    internal record PictureParams()
    {
        public int Width { get; set; } = default;

        public int Height { get; set; } = default;

        public int DpiX { get; set; } = default;

        public int DpiY { get; set; } = default;

        public IntPtr Buffer { get; set; } = default;
        
        public int Stride { get; set; } = default;

        public ILockedFramebuffer LockedFramebuffer { get; set; } = default;

        public AVPixelFormat PixelFormat { get; set; } = AVPixelFormat.AV_PIX_FMT_NONE;

        public WriteableBitmap CreateBitmap()
        {
            return new(new PixelSize(Width, Height), new Vector(DpiX, DpiY), Avalonia.Platform.PixelFormat.Bgra8888,
                null);
        }

        public PixelRect ToRect() => new(0, 0, Width, Height);

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
            //Buffer = bitmap.BackBuffer,
            Width = bitmap.PixelSize.Width,
            Height = bitmap.PixelSize.Height,
            DpiX = Convert.ToInt32(bitmap.Dpi.X),
            DpiY = Convert.ToInt32(bitmap.Dpi.Y),
            PixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA,
            //Stride = bitmap.BackBufferStride
        };
    }
}