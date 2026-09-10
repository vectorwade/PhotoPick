using System;
using System.Collections.Generic;
using System.Reflection;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class DoubtFilterTests
{
    [Fact]
    public void PhotoItem_IsDoubt_ReflectsColorLabelYellow()
    {
        var photo = new PhotoItem
        {
            FilePath = @"C:\Photos\DJI_0001.DNG",
            FileName = "DJI_0001.DNG",
            DirectoryPath = @"C:\Photos",
            Extension = ".DNG"
        };

        Assert.False(photo.IsDoubt);

        photo.IsDoubt = true;
        Assert.Equal("Yellow", photo.ColorLabel);
        Assert.True(photo.IsDoubt);

        photo.IsDoubt = false;
        Assert.Null(photo.ColorLabel);
        Assert.False(photo.IsDoubt);
    }

    [Fact]
    public void CullingSession_ToggleDoubt_UpdatesCountersAndAppliesFilter()
    {
        var session = new CullingSession();
        var photo1 = new PhotoItem { FilePath = "C:/test/P1.JPG", FileName = "P1.JPG", DirectoryPath = "C:/test", Extension = ".JPG" };
        var photo2 = new PhotoItem { FilePath = "C:/test/P2.JPG", FileName = "P2.JPG", DirectoryPath = "C:/test", Extension = ".JPG" };
        var photo3 = new PhotoItem { FilePath = "C:/test/P3.JPG", FileName = "P3.JPG", DirectoryPath = "C:/test", Extension = ".JPG" };

        var field = typeof(CullingSession).GetField("_allPhotos", BindingFlags.NonPublic | BindingFlags.Instance);
        var allPhotos = (List<PhotoItem>)field!.GetValue(session)!;
        allPhotos.AddRange(new[] { photo1, photo2, photo3 });

        // Inicialmente todas estão não avaliadas
        session.ApplyFilter(PhotoFilterMode.All);
        Assert.Equal(0, session.DoubtCount);
        Assert.Equal(3, session.UnflaggedCount);

        // Marca foto 1 como dúvida
        session.ToggleDoubt(photo1);
        Assert.Equal(1, session.DoubtCount);
        Assert.Equal(2, session.UnflaggedCount);
        Assert.True(photo1.IsDoubt);

        // Marca foto 2 como Pick
        session.TogglePick(photo2);
        Assert.Equal(1, session.PickedCount);
        Assert.Equal(1, session.UnflaggedCount);

        // Filtra somente Dúvida
        session.ApplyFilter(PhotoFilterMode.DoubtOnly);
        Assert.Single(session.FilteredPhotos);
        Assert.Equal("P1.JPG", session.FilteredPhotos[0].FileName);

        // Desmarca dúvida
        session.ToggleDoubt(photo1);
        Assert.Equal(0, session.DoubtCount);
        Assert.Equal(2, session.UnflaggedCount);
    }

    [Theory]
    [InlineData("DJI", "FC3582", "DJI Mini 3 Pro")]
    [InlineData("DJI", "FC3411", "DJI Air 2S")]
    [InlineData("DJI", "L2D-20c", "DJI Mavic 3 (Hasselblad)")]
    [InlineData("DJI", "FC220", "DJI Mavic Pro")]
    [InlineData("Sony", "ILCE-7M4", "Sony ILCE-7M4")]
    [InlineData("Nikon", "Z 6 III", "Nikon Z 6 III")]
    [InlineData("Canon", "EOS R5", "Canon EOS R5")]
    public void RawPreviewExtractor_NormalizeCameraModel_MapsCorrectly(string make, string model, string expected)
    {
        string? actual = RawPreviewExtractor.NormalizeCameraModel(make, model);
        Assert.Equal(expected, actual);
    }
}
