using System;
using System.Collections.Generic;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class BurstStackingTests
{
    [Fact]
    public void GroupBursts_GroupsConsecutiveRapidShots()
    {
        var baseTime = new DateTime(2026, 9, 9, 14, 0, 0);

        var photos = new List<PhotoItem>
        {
            new() { FilePath = "C:/photos/img01.cr3", FileName = "img01.cr3", DirectoryPath = "C:/photos", Extension = ".CR3", DateTaken = baseTime },
            new() { FilePath = "C:/photos/img02.cr3", FileName = "img02.cr3", DirectoryPath = "C:/photos", Extension = ".CR3", DateTaken = baseTime.AddSeconds(0.3) },
            new() { FilePath = "C:/photos/img03.cr3", FileName = "img03.cr3", DirectoryPath = "C:/photos", Extension = ".CR3", DateTaken = baseTime.AddSeconds(0.6) },
            // Intervalo longo de 10s
            new() { FilePath = "C:/photos/img04.cr3", FileName = "img04.cr3", DirectoryPath = "C:/photos", Extension = ".CR3", DateTaken = baseTime.AddSeconds(10.0) }
        };

        BurstStackingService.GroupBursts(photos, maxIntervalSeconds: 1.5);

        // Os três primeiros devem estar agrupados na mesma rajada
        Assert.NotNull(photos[0].BurstGroupId);
        Assert.Equal(photos[0].BurstGroupId, photos[1].BurstGroupId);
        Assert.Equal(photos[0].BurstGroupId, photos[2].BurstGroupId);
        Assert.Equal(3, photos[0].BurstTotal);
        Assert.True(photos[0].IsBurstLead);
        Assert.False(photos[1].IsBurstLead);

        // O quarto deve ser foto avulsa
        Assert.Null(photos[3].BurstGroupId);
        Assert.False(photos[3].IsInBurst);
    }

    [Fact]
    public void PickBestAndRejectRest_PicksChosenAndRejectsRestOfBurst()
    {
        var photos = new List<PhotoItem>
        {
            new() { FilePath = "f1.jpg", FileName = "f1.jpg", DirectoryPath = ".", Extension = ".JPG", BurstGroupId = "BURST_0001", BurstTotal = 3 },
            new() { FilePath = "f2.jpg", FileName = "f2.jpg", DirectoryPath = ".", Extension = ".JPG", BurstGroupId = "BURST_0001", BurstTotal = 3 },
            new() { FilePath = "f3.jpg", FileName = "f3.jpg", DirectoryPath = ".", Extension = ".JPG", BurstGroupId = "BURST_0001", BurstTotal = 3 }
        };

        // Escolhe a segunda foto da rajada
        var affected = BurstStackingService.PickBestAndRejectRest(photos[1], photos);

        Assert.True(photos[1].IsPicked);
        Assert.False(photos[1].IsRejected);

        Assert.False(photos[0].IsPicked);
        Assert.True(photos[0].IsRejected);

        Assert.False(photos[2].IsPicked);
        Assert.True(photos[2].IsRejected);
        Assert.Equal(3, affected.Count);
    }
}