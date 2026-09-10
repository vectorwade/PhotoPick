using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoPick.Desktop.Helpers;

public static class FocusPeakingHelper
{
    /// <summary>
    /// Gera uma máscara de contraste em alta velocidade destacando as bordas em foco (Focus Peaking)
    /// </summary>
    public static BitmapSource? GeneratePeakingOverlay(BitmapSource source, Color? highlightColor = null, int threshold = 75)
    {
        try
        {
            Color color = highlightColor ?? Color.FromArgb(240, 0, 255, 80); // Verde Neon por padrão

            // Otimização: se a imagem for gigantesca, reduz para até 1280px para rodar instantaneamente
            BitmapSource inputSource = source;
            if (source.PixelWidth > 1280)
            {
                double scale = 1280.0 / source.PixelWidth;
                var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                transformed.Freeze();
                inputSource = transformed;
            }

            var gray = new FormatConvertedBitmap(inputSource, PixelFormats.Gray8, null, 0);
            gray.Freeze();

            int width = gray.PixelWidth;
            int height = gray.PixelHeight;
            if (width < 3 || height < 3) return null;

            int grayStride = width;
            byte[] grayBytes = new byte[grayStride * height];
            gray.CopyPixels(grayBytes, grayStride, 0);

            // Cria imagem de saída Bgra32 com canal alfa
            var peakingBmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int bgraStride = width * 4;
            byte[] outPixels = new byte[bgraStride * height];

            byte b = color.B;
            byte g = color.G;
            byte r = color.R;
            byte a = color.A;

            // Filtro Sobel para detecção de bordas de contraste
            for (int y = 1; y < height - 1; y++)
            {
                int rowCurrent = y * width;
                int rowAbove = (y - 1) * width;
                int rowBelow = (y + 1) * width;
                int outRow = y * bgraStride;

                for (int x = 1; x < width - 1; x++)
                {
                    int gx = -grayBytes[rowAbove + x - 1] + grayBytes[rowAbove + x + 1]
                             - (2 * grayBytes[rowCurrent + x - 1]) + (2 * grayBytes[rowCurrent + x + 1])
                             - grayBytes[rowBelow + x - 1] + grayBytes[rowBelow + x + 1];

                    int gy = -grayBytes[rowAbove + x - 1] - (2 * grayBytes[rowAbove + x]) - grayBytes[rowAbove + x + 1]
                             + grayBytes[rowBelow + x - 1] + (2 * grayBytes[rowBelow + x]) + grayBytes[rowBelow + x + 1];

                    int mag = Math.Abs(gx) + Math.Abs(gy);

                    if (mag >= threshold)
                    {
                        int px = outRow + (x * 4);
                        outPixels[px + 0] = b; // Blue
                        outPixels[px + 1] = g; // Green
                        outPixels[px + 2] = r; // Red
                        outPixels[px + 3] = a; // Alpha
                    }
                }
            }

            peakingBmp.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), outPixels, bgraStride, 0);
            peakingBmp.Freeze();
            return peakingBmp;
        }
        catch
        {
            return null;
        }
    }
}
