using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class DeleteAndRatingTests
{
    [Fact]
    public void CullingSession_DeletePhoto_RemovesFromCollectionsAndUpdatesStats()
    {
        var session = new CullingSession();
        var photo1 = new PhotoItem { FilePath = "C:/test/P1.JPG", FileName = "P1.JPG", DirectoryPath = "C:/test", Extension = ".JPG" };
        var photo2 = new PhotoItem { FilePath = "C:/test/P2.JPG", FileName = "P2.JPG", DirectoryPath = "C:/test", Extension = ".JPG" };

        var field = typeof(CullingSession).GetField("_allPhotos", BindingFlags.NonPublic | BindingFlags.Instance);
        var allPhotos = (List<PhotoItem>)field!.GetValue(session)!;
        allPhotos.AddRange(new[] { photo1, photo2 });

        session.ApplyFilter(PhotoFilterMode.All);
        Assert.Equal(2, session.TotalCount);

        bool eventFired = false;
        session.StatsChanged += () => eventFired = true;

        bool removed = session.DeletePhoto(photo1);

        Assert.True(removed);
        Assert.Equal(1, session.TotalCount);
        Assert.DoesNotContain(photo1, session.FilteredPhotos);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task CullingSession_DeletePhotoAsync_DeletesFromDatabaseAndSession()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_delete_{Guid.NewGuid()}.db");
        var db = new CatalogDatabase(dbPath);
        var session = new CullingSession(database: db);

        var photo = new PhotoItem
        {
            FilePath = @"C:\Photos\IMG_0001.CR3",
            FileName = "IMG_0001.CR3",
            DirectoryPath = @"C:\Photos",
            Extension = ".CR3",
            Rating = 4
        };

        await db.SaveOrUpdatePhotosAsync(new[] { photo });

        var field = typeof(CullingSession).GetField("_allPhotos", BindingFlags.NonPublic | BindingFlags.Instance);
        var allPhotos = (List<PhotoItem>)field!.GetValue(session)!;
        allPhotos.Add(photo);

        session.ApplyFilter(PhotoFilterMode.All);
        Assert.Equal(1, session.TotalCount);

        bool removed = await session.DeletePhotoAsync(photo);

        Assert.True(removed);
        Assert.Equal(0, session.TotalCount);

        // Verifica que saiu do banco SQLite
        var cached = await db.GetCachedPhotoDataForDirectoryAsync(@"C:\Photos");
        Assert.Empty(cached);

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
    }

    [Fact]
    public void Rating_ToggleBehavior_ReflectsCorrectly()
    {
        var photo = new PhotoItem
        {
            FilePath = @"C:\Photos\IMG_0002.CR3",
            FileName = "IMG_0002.CR3",
            DirectoryPath = @"C:\Photos",
            Extension = ".CR3",
            Rating = 0
        };

        Assert.Equal(0, photo.Rating);
        Assert.Equal("☆☆☆☆☆", photo.RatingStars);

        // Atribuir 3 estrelas
        photo.Rating = 3;
        Assert.Equal(3, photo.Rating);
        Assert.Equal("★★★☆☆", photo.RatingStars);

        // Clicar novamente na estrela 3 zera a avaliação
        int newRating = (photo.Rating == 3) ? 0 : 3;
        photo.Rating = newRating;
        Assert.Equal(0, photo.Rating);
        Assert.Equal("☆☆☆☆☆", photo.RatingStars);
    }
}