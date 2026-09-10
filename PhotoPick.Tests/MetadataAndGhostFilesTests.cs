using System;
using System.IO;
using System.Threading.Tasks;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class MetadataAndGhostFilesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly CatalogDatabase _database;

    public MetadataAndGhostFilesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MetadataGhostTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_catalog.db");
        _database = new CatalogDatabase(_dbPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void RawPreviewExtractor_IsSupported_RejectsAppleDoubleAndDotFiles()
    {
        Assert.False(RawPreviewExtractor.IsSupported(@"H:\DCIM\._DJI_001.DNG"));
        Assert.False(RawPreviewExtractor.IsSupported(@"C:\Photos\._test.cr3"));
        Assert.False(RawPreviewExtractor.IsSupported(@"C:\Photos\.hidden.arw"));
        Assert.True(RawPreviewExtractor.IsSupported(@"H:\DCIM\DJI_001.DNG"));
        Assert.True(RawPreviewExtractor.IsSupported(@"C:\Photos\photo.cr3"));
    }

    [Fact]
    public async Task CatalogDatabase_RemoveGhostFilesAsync_DeletesAppleDoubleAndSmallFiles()
    {
        string dir = Path.Combine(_tempDir, "Album");
        Directory.CreateDirectory(dir);

        var realPhoto = new PhotoItem
        {
            FilePath = Path.Combine(dir, "DJI_0001.DNG"),
            FileName = "DJI_0001.DNG",
            DirectoryPath = dir,
            Extension = ".DNG",
            FileSize = 50_000_000,
            CameraModel = "DJI Neo",
            CameraMake = "DJI",
            LensModel = "DJI Neo Lens 8.7mm",
            Iso = 100,
            FNumber = 1.8,
            ExposureTime = 0.001,
            FocalLength = 8.7,
            ExposureBias = 0.0
        };

        var ghostAppleDouble = new PhotoItem
        {
            FilePath = Path.Combine(dir, "._DJI_0001.DNG"),
            FileName = "._DJI_0001.DNG",
            DirectoryPath = dir,
            Extension = ".DNG",
            FileSize = 4096
        };

        var zeroByteCorrupt = new PhotoItem
        {
            FilePath = Path.Combine(dir, "corrupted.jpg"),
            FileName = "corrupted.jpg",
            DirectoryPath = dir,
            Extension = ".JPG",
            FileSize = 512
        };

        await _database.SaveOrUpdatePhotosAsync([realPhoto, ghostAppleDouble, zeroByteCorrupt]);

        // Verifica inserção
        var cachedBefore = await _database.GetCachedPhotoDataForDirectoryAsync(dir);
        Assert.Equal(3, cachedBefore.Count);

        // Remove arquivos fantasmas
        await _database.RemoveGhostFilesAsync(dir);

        var cachedAfter = await _database.GetCachedPhotoDataForDirectoryAsync(dir);
        Assert.Single(cachedAfter);
        Assert.True(cachedAfter.ContainsKey(realPhoto.FilePath));

        var meta = cachedAfter[realPhoto.FilePath];
        Assert.Equal("DJI Neo", meta.CameraModel);
        Assert.Equal("DJI", meta.CameraMake);
        Assert.Equal("DJI Neo Lens 8.7mm", meta.LensModel);
        Assert.Equal(100, meta.Iso);
        Assert.Equal(1.8, meta.FNumber);
        Assert.Equal(0.001, meta.ExposureTime);
        Assert.Equal(8.7, meta.FocalLength);
        Assert.Equal(0.0, meta.ExposureBias);
    }

    [Fact]
    public void PhotoItem_MetadataProperties_RaisePropertyChanged()
    {
        var item = new PhotoItem
        {
            FilePath = "test.dng",
            FileName = "test.dng",
            DirectoryPath = @"C:\Photos",
            Extension = ".DNG"
        };

        string? changedProperty = null;
        item.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

        item.CameraModel = "Sony A7 IV";
        Assert.Equal(nameof(PhotoItem.CameraModel), changedProperty);

        item.LensModel = "FE 24-70mm F2.8 GM II";
        Assert.Equal(nameof(PhotoItem.LensModel), changedProperty);

        item.Iso = 400;
        Assert.Equal(nameof(PhotoItem.Iso), changedProperty);

        item.FNumber = 2.8;
        Assert.Equal(nameof(PhotoItem.FNumber), changedProperty);

        item.ExposureTime = 0.004;
        Assert.Equal(nameof(PhotoItem.ExposureTime), changedProperty);

        item.FocalLength = 50.0;
        Assert.Equal(nameof(PhotoItem.FocalLength), changedProperty);

        item.ExposureBias = -0.3;
        Assert.Equal(nameof(PhotoItem.ExposureBias), changedProperty);

        item.FlashFired = true;
        Assert.Equal(nameof(PhotoItem.FlashFired), changedProperty);

        item.Width = 7000;
        Assert.Equal(nameof(PhotoItem.Width), changedProperty);

        item.Height = 4667;
        Assert.Equal(nameof(PhotoItem.Height), changedProperty);
    }
}
