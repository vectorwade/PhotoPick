using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;

namespace PhotoPick.Tests;

public class DngDebugTests
{
    [Fact]
    public void TestDjiDngExtraction()
    {
        string path = @"H:\DCIM\DJI_001\DJI_20260730101639_0005_D.DNG";
        if (!File.Exists(path)) return;

        var extractor = new RawPreviewExtractor();
        var result = extractor.ExtractPreview(path);
        
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("DJI Neo", result.CameraModel);
        Assert.Equal("DJI", result.CameraMake);
        Assert.NotNull(result.LensModel);
        Assert.Contains("DJI", result.LensModel);
        Assert.True(result.Iso.HasValue);
        Assert.True(result.FNumber.HasValue);
        Assert.True(result.ExposureTime.HasValue);
        Assert.True(result.FocalLength.HasValue);
        Assert.Equal(24, result.FocalLength35mm);
        Assert.Equal("Média", result.MeteringMode);
        Assert.Equal(1.7, result.MaxAperture);
    }

    [Fact]
    public async Task BackfillDjiDatabase()
    {
        string dir = @"H:\DCIM\DJI_001";
        if (!Directory.Exists(dir)) return;

        var extractor = new RawPreviewExtractor();
        var db = new CatalogDatabase();
        var files = Directory.GetFiles(dir, "*.DNG");

        var items = new System.Collections.Generic.List<PhotoItem>();
        foreach (var f in files)
        {
            var res = extractor.ExtractPreview(f);
            if (res.Success)
            {
                var fi = new FileInfo(f);
                var item = new PhotoItem
                {
                    FilePath = f,
                    FileName = fi.Name,
                    DirectoryPath = dir,
                    Extension = fi.Extension.ToUpperInvariant(),
                    FileSize = fi.Length,
                    Orientation = res.Orientation,
                    Width = res.Width,
                    Height = res.Height,
                    CameraModel = res.CameraModel,
                    CameraMake = res.CameraMake,
                    LensModel = res.LensModel,
                    Iso = res.Iso,
                    FNumber = res.FNumber,
                    ExposureTime = res.ExposureTime,
                    FocalLength = res.FocalLength,
                    ExposureBias = res.ExposureBias,
                    FlashFired = res.FlashFired,
                    DateTaken = res.DateTaken,
                    FocalLength35mm = res.FocalLength35mm,
                    MaxAperture = res.MaxAperture,
                    MeteringMode = res.MeteringMode,
                    ExposureProgram = res.ExposureProgram,
                    ExposureMode = res.ExposureMode,
                    WhiteBalance = res.WhiteBalance,
                    Software = res.Software,
                    SerialNumber = res.SerialNumber
                };
                items.Add(item);
            }
        }

        if (items.Count > 0)
        {
            await db.SaveOrUpdatePhotosAsync(items);
        }

        Assert.NotEmpty(items);
    }

    [Fact]
    public async Task TestCullingSessionLoadDjiFolder()
    {
        string dir = @"H:\DCIM\DJI_001";
        if (!Directory.Exists(dir)) return;

        var session = new CullingSession();
        int statsTriggerCount = 0;
        session.StatsChanged += () => statsTriggerCount++;

        int count = await session.LoadDirectoryAsync(dir);
        Assert.True(count > 0);
        Assert.True(session.TotalCount > 0);
        Assert.True(statsTriggerCount > 0);
        Assert.NotEmpty(session.AvailableCameras);
    }
}

