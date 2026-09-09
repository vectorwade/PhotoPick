using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoPick.Desktop.Helpers;

public static class ImageHelper
{
    public static BitmapSource? LoadBitmapFromBytes(byte[] bytes, int orientation = 1, int decodePixelWidth = 0)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            if (decodePixelWidth > 0)
            {
                bi.DecodePixelWidth = decodePixelWidth;
            }
            bi.EndInit();
            bi.Freeze();

            return ApplyOrientation(bi, orientation);
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? LoadBitmapFromFile(string filePath, int orientation = 1, int decodePixelWidth = 0)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = stream;
            if (decodePixelWidth > 0)
            {
                bi.DecodePixelWidth = decodePixelWidth;
            }
            bi.EndInit();
            bi.Freeze();

            return ApplyOrientation(bi, orientation);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        if (orientation <= 1) return source;

        Transform? transform = orientation switch
        {
            3 => new RotateTransform(180),
            6 => new RotateTransform(90),
            8 => new RotateTransform(270),
            _ => null
        };

        if (transform == null) return source;

        var tb = new TransformedBitmap(source, transform);
        tb.Freeze();
        return tb;
    }
}
