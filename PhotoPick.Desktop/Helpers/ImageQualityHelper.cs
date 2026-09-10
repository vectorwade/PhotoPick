using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;

namespace PhotoPick.Desktop.Helpers;

public static class ImageQualityHelper
{
    /// <summary>
    /// Analisa métricas de nitidez (foco) e luminosidade diretamente da imagem em escala de cinza
    /// </summary>
    public static void AnalyzeAndApply(BitmapSource bitmap, PhotoItem item)
    {
        try
        {
            // Reduz amostragem se a imagem for grande para gastar menos de 0.1ms
            BitmapSource source = bitmap;
            if (bitmap.PixelWidth > 320)
            {
                var tb = new TransformedBitmap(bitmap, new ScaleTransform(320.0 / bitmap.PixelWidth, 320.0 / bitmap.PixelWidth));
                tb.Freeze();
                source = tb;
            }

            var grayBmp = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            grayBmp.Freeze();

            int width = grayBmp.PixelWidth;
            int height = grayBmp.PixelHeight;
            if (width < 5 || height < 5) return;

            int stride = width;
            byte[] grayPixels = new byte[stride * height];
            grayBmp.CopyPixels(grayPixels, stride, 0);

            var result = ImageQualityAnalyzer.AnalyzeGrayscale(grayPixels, width, height);

            item.SharpnessScore = result.SharpnessScore;
            item.BrightnessScore = result.BrightnessScore;
            item.IsBlurry = result.IsBlurry;
            item.IsUnderexposed = result.IsUnderexposed;
            item.IsOverexposed = result.IsOverexposed;
        }
        catch
        {
            // Não interrompe o fluxo caso ocorra exceção gráfica
        }
    }
}
