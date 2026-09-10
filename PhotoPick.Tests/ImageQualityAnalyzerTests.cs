using System;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class ImageQualityAnalyzerTests
{
    [Fact]
    public void AnalyzeGrayscale_DetectsUnderexposedDarkImage()
    {
        // Cria imagem 100x100 quase toda escura (luma = 10)
        int width = 100;
        int height = 100;
        byte[] darkPixels = new byte[width * height];
        Array.Fill(darkPixels, (byte)10);

        var result = ImageQualityAnalyzer.AnalyzeGrayscale(darkPixels, width, height);

        Assert.True(result.IsUnderexposed);
        Assert.False(result.IsOverexposed);
        Assert.True(result.HasDefect);
        Assert.False(result.IsGoodQuality);
    }

    [Fact]
    public void AnalyzeGrayscale_DetectsOverexposedBlownImage()
    {
        // Cria imagem 100x100 quase toda estourada (luma = 245)
        int width = 100;
        int height = 100;
        byte[] brightPixels = new byte[width * height];
        Array.Fill(brightPixels, (byte)245);

        var result = ImageQualityAnalyzer.AnalyzeGrayscale(brightPixels, width, height);

        Assert.True(result.IsOverexposed);
        Assert.False(result.IsUnderexposed);
        Assert.True(result.HasDefect);
    }

    [Fact]
    public void AnalyzeGrayscale_DetectsBlurryImageWithoutEdges()
    {
        // Imagem uniforme sem contraste nem bordas (luma = 120 uniforme)
        int width = 100;
        int height = 100;
        byte[] flatPixels = new byte[width * height];
        Array.Fill(flatPixels, (byte)120);

        var result = ImageQualityAnalyzer.AnalyzeGrayscale(flatPixels, width, height);

        Assert.True(result.IsBlurry);
        Assert.Equal(0, result.SharpnessScore);
    }

    [Fact]
    public void AnalyzeGrayscale_RecognizesSharpWellExposedImage()
    {
        // Imagem com padrão quadriculado de alto contraste (foco nítido) e boa exposição
        int width = 100;
        int height = 100;
        byte[] checkerboard = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                checkerboard[y * width + x] = ((x / 4) + (y / 4)) % 2 == 0 ? (byte)60 : (byte)180;
            }
        }

        var result = ImageQualityAnalyzer.AnalyzeGrayscale(checkerboard, width, height);

        Assert.False(result.IsBlurry);
        Assert.False(result.IsUnderexposed);
        Assert.False(result.IsOverexposed);
        Assert.True(result.IsGoodQuality);
        Assert.True(result.SharpnessScore > 50);
    }
}