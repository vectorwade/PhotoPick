using System;

namespace PhotoPick.Core.Services;

public record ImageQualityResult(
    double SharpnessScore,
    double BrightnessScore,
    bool IsBlurry,
    bool IsUnderexposed,
    bool IsOverexposed
)
{
    public bool HasDefect => IsBlurry || IsUnderexposed || IsOverexposed;
    public bool IsGoodQuality => !HasDefect;
}

public static class ImageQualityAnalyzer
{
    // Limiares calibrados para fotografia profissional
    public const double DefaultBlurryThreshold = 45.0;
    public const double DefaultUnderexposedThreshold = 42.0;
    public const double DefaultOverexposedThreshold = 215.0;

    /// <summary>
    /// Analisa matriz de pixels em escala de cinza (8 bits por pixel)
    /// </summary>
    public static ImageQualityResult AnalyzeGrayscale(
        ReadOnlySpan<byte> grayPixels, 
        int width, 
        int height,
        double blurryThreshold = DefaultBlurryThreshold,
        double underThreshold = DefaultUnderexposedThreshold,
        double overThreshold = DefaultOverexposedThreshold)
    {
        if (width < 3 || height < 3 || grayPixels.Length < width * height)
        {
            return new ImageQualityResult(0, 128, false, false, false);
        }

        int totalPixels = width * height;
        long sumLuma = 0;
        int shadowCount = 0;
        int highlightCount = 0;

        for (int i = 0; i < totalPixels; i++)
        {
            byte val = grayPixels[i];
            sumLuma += val;
            if (val < 15) shadowCount++;
            if (val > 240) highlightCount++;
        }

        double meanLuma = (double)sumLuma / totalPixels;

        // Cálculo da Variância do Laplaciano para Nitidez (Laplacian Sharpness Metric)
        // Kernel Laplaciano 3x3:
        // [  0,  1,  0 ]
        // [  1, -4,  1 ]
        // [  0,  1,  0 ]
        double sumLaplacian = 0;
        double sumSqLaplacian = 0;
        int laplacianCount = 0;

        // Amostragem com salto para velocidade extrema se a imagem for grande
        int step = (width > 300 || height > 300) ? 2 : 1;

        for (int y = 1; y < height - 1; y += step)
        {
            int rowCurrent = y * width;
            int rowAbove = (y - 1) * width;
            int rowBelow = (y + 1) * width;

            for (int x = 1; x < width - 1; x += step)
            {
                int center = grayPixels[rowCurrent + x];
                int top = grayPixels[rowAbove + x];
                int bottom = grayPixels[rowBelow + x];
                int left = grayPixels[rowCurrent + x - 1];
                int right = grayPixels[rowCurrent + x + 1];

                int laplacianVal = top + bottom + left + right - (4 * center);
                sumLaplacian += laplacianVal;
                sumSqLaplacian += (double)laplacianVal * laplacianVal;
                laplacianCount++;
            }
        }

        double sharpnessScore = 0;
        if (laplacianCount > 0)
        {
            double meanLap = sumLaplacian / laplacianCount;
            double variance = (sumSqLaplacian / laplacianCount) - (meanLap * meanLap);
            sharpnessScore = Math.Max(0, variance);
        }

        // Critérios de defeito
        bool isUnderexposed = meanLuma < underThreshold || (shadowCount > totalPixels * 0.40 && meanLuma < 70);
        bool isOverexposed = meanLuma > overThreshold || (highlightCount > totalPixels * 0.35 && meanLuma > 185);
        bool isBlurry = sharpnessScore < blurryThreshold && !isUnderexposed && !isOverexposed;

        return new ImageQualityResult(
            SharpnessScore: Math.Round(sharpnessScore, 2),
            BrightnessScore: Math.Round(meanLuma, 2),
            IsBlurry: isBlurry,
            IsUnderexposed: isUnderexposed,
            IsOverexposed: isOverexposed
        );
    }
}
