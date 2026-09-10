using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PhotoPick.Core.Models;
using PhotoPick.Desktop.Helpers;

namespace PhotoPick.Desktop.Services;

public class PredictivePrecacheService
{
    private readonly ConcurrentDictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lruKeys = new();
    private readonly object _lock = new();
    private const int MaxCacheSize = 10;
    private CancellationTokenSource? _precacheCts;

    public bool TryGet(string filePath, out BitmapSource? bitmap)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(filePath, out bitmap))
            {
                _lruKeys.Remove(filePath);
                _lruKeys.AddLast(filePath);
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    public void Store(string filePath, BitmapSource bitmap)
    {
        lock (_lock)
        {
            if (_cache.TryAdd(filePath, bitmap))
            {
                _lruKeys.AddLast(filePath);
                if (_lruKeys.Count > MaxCacheSize)
                {
                    string oldest = _lruKeys.First!.Value;
                    _lruKeys.RemoveFirst();
                    _cache.TryRemove(oldest, out _);
                }
            }
        }
    }

    /// <summary>
    /// Pré-carrega assincronamente as 3 próximas fotos e a foto anterior
    /// </summary>
    public void SchedulePrecache(IList<PhotoItem> photos, int currentIndex)
    {
        _precacheCts?.Cancel();
        _precacheCts = new CancellationTokenSource();
        var ct = _precacheCts.Token;

        var indicesToPrecache = new List<int>
        {
            currentIndex + 1,
            currentIndex + 2,
            currentIndex + 3,
            currentIndex - 1
        };

        Task.Run(() =>
        {
            foreach (var idx in indicesToPrecache)
            {
                if (ct.IsCancellationRequested) break;
                if (idx < 0 || idx >= photos.Count) continue;

                var item = photos[idx];
                lock (_lock)
                {
                    if (_cache.ContainsKey(item.FilePath)) continue;
                }

                try
                {
                    if (!string.IsNullOrEmpty(item.ThumbnailCachePath) && System.IO.File.Exists(item.ThumbnailCachePath))
                    {
                        var bmp = ImageHelper.LoadBitmapFromFile(item.ThumbnailCachePath, item.Orientation, decodePixelWidth: 1600);
                        if (bmp != null)
                        {
                            Store(item.FilePath, bmp);
                        }
                    }
                }
                catch { }
            }
        }, ct);
    }
}
