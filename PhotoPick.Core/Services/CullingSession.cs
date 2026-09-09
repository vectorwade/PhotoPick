using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PhotoPick.Core.Models;

namespace PhotoPick.Core.Services;

public enum PhotoFilterMode
{
    All,
    PickedOnly,
    UnflaggedOnly,
    RatedOnly,
    RejectedOnly
}

public class CullingSession
{
    private readonly IRawPreviewExtractor _extractor;
    private readonly IXmpService _xmpService;
    private readonly IThumbnailCacheService _cacheService;
    private readonly CatalogDatabase _database;

    private readonly List<PhotoItem> _allPhotos = [];
    private CancellationTokenSource? _bgThumbCts;

    public ObservableCollection<PhotoItem> FilteredPhotos { get; } = [];
    public PhotoFilterMode CurrentFilter { get; private set; } = PhotoFilterMode.All;
    public string? CurrentDirectory { get; private set; }

    public int TotalCount => _allPhotos.Count;
    public int PickedCount => _allPhotos.Count(p => p.IsPicked);
    public int RejectedCount => _allPhotos.Count(p => p.IsRejected);
    public int UnflaggedCount => _allPhotos.Count(p => !p.IsPicked && !p.IsRejected && p.Rating == 0);
    public int UnsavedCount => _allPhotos.Count(p => p.IsModified);

    public event Action? StatsChanged;

    public CullingSession(
        IRawPreviewExtractor? extractor = null,
        IXmpService? xmpService = null,
        IThumbnailCacheService? cacheService = null,
        CatalogDatabase? database = null)
    {
        _extractor = extractor ?? new RawPreviewExtractor();
        _xmpService = xmpService ?? new XmpService();
        _cacheService = cacheService ?? new ThumbnailCacheService();
        _database = database ?? new CatalogDatabase();
    }

    public async Task<int> LoadDirectoryAsync(string directoryPath, IProgress<(int current, int total)>? progress = null, CancellationToken ct = default)
    {
        _bgThumbCts?.Cancel();
        _bgThumbCts = new CancellationTokenSource();

        CurrentDirectory = directoryPath;
        _allPhotos.Clear();
        FilteredPhotos.Clear();

        if (!Directory.Exists(directoryPath))
        {
            StatsChanged?.Invoke();
            return 0;
        }

        // 1. Obter dados já cacheados no SQLite
        var cachedData = await _database.GetCachedPhotoDataForDirectoryAsync(directoryPath);

        // 2. Enumerar arquivos suportados
        var files = Directory.EnumerateFiles(directoryPath)
            .Where(RawPreviewExtractor.IsSupported)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int total = files.Count;
        int index = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var fi = new FileInfo(file);
            var item = new PhotoItem
            {
                FilePath = file,
                FileName = fi.Name,
                DirectoryPath = directoryPath,
                Extension = fi.Extension.ToUpperInvariant(),
                FileSize = fi.Length
            };

            // Verificar se há dados no banco ou no XMP
            if (cachedData.TryGetValue(file, out var cacheInfo))
            {
                item.Rating = cacheInfo.rating;
                item.ColorLabel = cacheInfo.label;
                item.ThumbnailCachePath = cacheInfo.thumb;
                item.Orientation = cacheInfo.orient;
                item.Width = cacheInfo.width;
                item.Height = cacheInfo.height;
            }

            // Se existir XMP no disco, ele tem prioridade sobre o banco SQLite
            if (_xmpService.XmpExists(file))
            {
                var xmpMeta = _xmpService.ReadMetadata(file);
                item.Rating = xmpMeta.Rating;
                if (!string.IsNullOrEmpty(xmpMeta.Label))
                {
                    item.ColorLabel = xmpMeta.Label;
                }
            }

            // Verificar cache de thumbnail em disco se ainda não preenchido
            if (string.IsNullOrEmpty(item.ThumbnailCachePath))
            {
                item.ThumbnailCachePath = _cacheService.GetCachedThumbnailPath(file);
            }

            _allPhotos.Add(item);
            index++;
            progress?.Report((index, total));
        }

        ApplyFilter(CurrentFilter);
        StatsChanged?.Invoke();

        // Iniciar extração de thumbnails em background para os itens sem cache
        _ = StartBackgroundThumbnailExtractionAsync(_bgThumbCts.Token);

        return _allPhotos.Count;
    }

    public void ApplyFilter(PhotoFilterMode mode)
    {
        CurrentFilter = mode;
        FilteredPhotos.Clear();

        var query = mode switch
        {
            PhotoFilterMode.PickedOnly => _allPhotos.Where(p => p.IsPicked),
            PhotoFilterMode.RejectedOnly => _allPhotos.Where(p => p.IsRejected),
            PhotoFilterMode.UnflaggedOnly => _allPhotos.Where(p => !p.IsPicked && !p.IsRejected && p.Rating == 0),
            PhotoFilterMode.RatedOnly => _allPhotos.Where(p => p.Rating > 0),
            _ => _allPhotos.AsEnumerable()
        };

        foreach (var photo in query)
        {
            FilteredPhotos.Add(photo);
        }

        StatsChanged?.Invoke();
    }

    public void SetRating(PhotoItem item, int rating)
    {
        item.Rating = rating;
        item.IsModified = true;
        StatsChanged?.Invoke();
    }

    public void SetColorLabel(PhotoItem item, string? label)
    {
        item.ColorLabel = label;
        item.IsModified = true;
        StatsChanged?.Invoke();
    }

    public void TogglePick(PhotoItem item)
    {
        item.IsPicked = !item.IsPicked;
        item.IsModified = true;
        StatsChanged?.Invoke();
    }

    public void ToggleReject(PhotoItem item)
    {
        item.IsRejected = !item.IsRejected;
        item.IsModified = true;
        StatsChanged?.Invoke();
    }

    public async Task<int> SaveModifiedXmpAsync(CancellationToken ct = default)
    {
        var modified = _allPhotos.Where(p => p.IsModified).ToList();
        if (modified.Count == 0) return 0;

        await _xmpService.WriteMetadataBatchAsync(modified, ct);
        await _database.SaveOrUpdatePhotosAsync(modified);

        StatsChanged?.Invoke();
        return modified.Count;
    }

    public async Task<int> SaveAllXmpAsync(CancellationToken ct = default)
    {
        if (_allPhotos.Count == 0) return 0;

        await _xmpService.WriteMetadataBatchAsync(_allPhotos, ct);
        await _database.SaveOrUpdatePhotosAsync(_allPhotos);

        StatsChanged?.Invoke();
        return _allPhotos.Count;
    }

    public async Task<string?> EnsureThumbnailAsync(PhotoItem item, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(item.ThumbnailCachePath) && File.Exists(item.ThumbnailCachePath))
        {
            return item.ThumbnailCachePath;
        }

        string? diskCached = _cacheService.GetCachedThumbnailPath(item.FilePath);
        if (diskCached != null)
        {
            item.ThumbnailCachePath = diskCached;
            return diskCached;
        }

        item.IsLoadingThumbnail = true;
        try
        {
            var res = await _extractor.ExtractPreviewAsync(item.FilePath, ct);
            if (res.Success && res.JpegBytes != null)
            {
                item.Orientation = res.Orientation;
                item.Width = res.Width;
                item.Height = res.Height;

                string savedPath = await _cacheService.SaveThumbnailAsync(item.FilePath, res.JpegBytes);
                item.ThumbnailCachePath = savedPath;
                return savedPath;
            }
        }
        finally
        {
            item.IsLoadingThumbnail = false;
        }

        return null;
    }

    private async Task StartBackgroundThumbnailExtractionAsync(CancellationToken ct)
    {
        var missing = _allPhotos.Where(p => string.IsNullOrEmpty(p.ThumbnailCachePath) || !File.Exists(p.ThumbnailCachePath)).ToList();
        if (missing.Count == 0) return;

        // Limita a concorrência para não saturar I/O do disco
        using var semaphore = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));
        var tasks = missing.Select(async photo =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                await EnsureThumbnailAsync(photo, ct);
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Salva estado de thumbnails atualizado no banco
        try
        {
            await _database.SaveOrUpdatePhotosAsync(_allPhotos);
        }
        catch { }
    }
}
