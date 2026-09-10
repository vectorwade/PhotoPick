using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhotoPick.Core.Models;

namespace PhotoPick.Core.Services;

public static class BurstStackingService
{
    public const double DefaultBurstIntervalSeconds = 1.5;

    /// <summary>
    /// Identifica e agrupa fotos disparadas em sequência contínua (rajadas).
    /// </summary>
    public static void GroupBursts(IList<PhotoItem> photos, double maxIntervalSeconds = DefaultBurstIntervalSeconds)
    {
        if (photos.Count <= 1) return;

        // Ordena por data/hora se disponível, ou por nome de arquivo
        var ordered = photos
            .OrderBy(p => p.DateTaken ?? GetFallbackFileTime(p.FilePath))
            .ThenBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int currentBurstId = 1;
        List<PhotoItem> currentBurst = [];

        DateTime? lastTime = null;

        foreach (var item in ordered)
        {
            DateTime itemTime = item.DateTaken ?? GetFallbackFileTime(item.FilePath);

            if (lastTime == null)
            {
                currentBurst.Add(item);
            }
            else
            {
                double delta = (itemTime - lastTime.Value).TotalSeconds;
                if (delta >= 0 && delta <= maxIntervalSeconds)
                {
                    currentBurst.Add(item);
                }
                else
                {
                    FinalizeBurst(currentBurst, ref currentBurstId);
                    currentBurst.Clear();
                    currentBurst.Add(item);
                }
            }

            lastTime = itemTime;
        }

        FinalizeBurst(currentBurst, ref currentBurstId);
    }

    private static void FinalizeBurst(List<PhotoItem> burst, ref int currentBurstId)
    {
        if (burst.Count > 1)
        {
            string groupId = $"BURST_{currentBurstId:D4}";
            currentBurstId++;

            for (int i = 0; i < burst.Count; i++)
            {
                var item = burst[i];
                item.BurstGroupId = groupId;
                item.BurstIndex = i + 1;
                item.BurstTotal = burst.Count;
                item.IsBurstLead = (i == 0);
            }
        }
        else if (burst.Count == 1)
        {
            var item = burst[0];
            item.BurstGroupId = null;
            item.BurstIndex = 1;
            item.BurstTotal = 1;
            item.IsBurstLead = false;
        }
    }

    private static DateTime GetFallbackFileTime(string filePath)
    {
        try
        {
            return File.GetLastWriteTime(filePath);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Seleciona a foto escolhida (Pick) e rejeita automaticamente todas as outras fotos da mesma rajada.
    /// </summary>
    public static List<PhotoItem> PickBestAndRejectRest(PhotoItem chosenItem, IEnumerable<PhotoItem> allPhotos)
    {
        var affected = new List<PhotoItem>();

        if (string.IsNullOrEmpty(chosenItem.BurstGroupId))
        {
            chosenItem.IsPicked = true;
            chosenItem.IsModified = true;
            affected.Add(chosenItem);
            return affected;
        }

        chosenItem.IsPicked = true;
        chosenItem.IsModified = true;
        affected.Add(chosenItem);

        foreach (var other in allPhotos)
        {
            if (other != chosenItem && other.BurstGroupId == chosenItem.BurstGroupId)
            {
                other.IsRejected = true;
                other.IsModified = true;
                affected.Add(other);
            }
        }

        return affected;
    }
}
