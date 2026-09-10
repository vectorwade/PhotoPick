using System.IO;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class FormatFilterTests
{
    [Fact]
    public void FormatCounts_GroupsExtensionsCorrectly()
    {
        var session = new CullingSession();
        // Adiciona fotos de teste via reflexão ou mock
        var photo1 = new PhotoItem { FilePath = "C:/test/img1.CR3", FileName = "img1.CR3", DirectoryPath = "C:/test", Extension = ".CR3" };
        var photo2 = new PhotoItem { FilePath = "C:/test/img2.CR3", FileName = "img2.CR3", DirectoryPath = "C:/test", Extension = ".CR3" };
        var photo3 = new PhotoItem { FilePath = "C:/test/img3.ARW", FileName = "img3.ARW", DirectoryPath = "C:/test", Extension = ".ARW" };
        var photo4 = new PhotoItem { FilePath = "C:/test/img4.JPG", FileName = "img4.JPG", DirectoryPath = "C:/test", Extension = ".JPG" };

        var field = typeof(CullingSession).GetField("_allPhotos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var allPhotos = (System.Collections.Generic.List<PhotoItem>)field!.GetValue(session)!;
        allPhotos.AddRange(new[] { photo1, photo2, photo3, photo4 });

        var counts = session.FormatCounts;

        Assert.Equal(3, counts.Count);
        Assert.Equal(2, counts[".CR3"]);
        Assert.Equal(1, counts[".ARW"]);
        Assert.Equal(1, counts[".JPG"]);
    }

    [Fact]
    public void SetFormatFilter_FiltersPhotosAccurately()
    {
        var session = new CullingSession();
        var photo1 = new PhotoItem { FilePath = "C:/test/img1.CR3", FileName = "img1.CR3", DirectoryPath = "C:/test", Extension = ".CR3" };
        var photo2 = new PhotoItem { FilePath = "C:/test/img2.CR3", FileName = "img2.CR3", DirectoryPath = "C:/test", Extension = ".CR3" };
        var photo3 = new PhotoItem { FilePath = "C:/test/img3.ARW", FileName = "img3.ARW", DirectoryPath = "C:/test", Extension = ".ARW" };

        var field = typeof(CullingSession).GetField("_allPhotos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var allPhotos = (System.Collections.Generic.List<PhotoItem>)field!.GetValue(session)!;
        allPhotos.AddRange(new[] { photo1, photo2, photo3 });

        // Filtra por .CR3
        session.SetFormatFilter(".CR3");
        Assert.Equal(2, session.FilteredPhotos.Count);
        Assert.All(session.FilteredPhotos, p => Assert.Equal(".CR3", p.Extension));

        // Filtra por .ARW
        session.SetFormatFilter(".ARW");
        Assert.Single(session.FilteredPhotos);
        Assert.Equal("img3.ARW", session.FilteredPhotos[0].FileName);

        // Reseta para ALL
        session.SetFormatFilter("ALL");
        Assert.Equal(3, session.FilteredPhotos.Count);
    }
}
