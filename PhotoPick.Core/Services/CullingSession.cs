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
    GoodOnly,
    BlurryOnly,
    UnderexposedOnly,
    OverexposedOnly,
    BurstStacksOnly,
    PickedOnly,
    DoubtOnly,
    UnflaggedOnly,
    RatedOnly,
    RejectedOnly,
    Rating5,
    Rating4,
    Rating3,
    Rating2,
    Rating1
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
    public string? CurrentCameraFilter { get; private set; }
    public bool? CurrentFlashFilter { get; private set; }
    public string? CurrentFormatFilter { get; private set; }

    public IRawPreviewExtractor Extractor => _extractor;
    public IThumbnailCacheService CacheService => _cacheService;

    public Dictionary<string, int> FormatCounts => _allPhotos
        .GroupBy(p => p.Extension.ToUpperInvariant())
        .ToDictionary(g => g.Key, g => g.Count());
    public string? CurrentDirectory { get; private set; }

    public int TotalCount => _allPhotos.Count;
    public int PickedCount => _allPhotos.Count(p => p.IsPicked);
    public int DoubtCount => _allPhotos.Count(p => p.IsDoubt);
    public int RejectedCount => _allPhotos.Count(p => p.IsRejected);
    public int UnflaggedCount => _allPhotos.Count(p => !p.IsPicked && !p.IsRejected && !p.IsDoubt && p.Rating == 0);
    public int UnsavedCount => _allPhotos.Count(p => p.IsModified);

    // Contagens de Qualidade e Rajadas
    public int GoodCount => _allPhotos.Count(p => p.IsGoodQuality);
    public int BlurryCount => _allPhotos.Count(p => p.IsBlurry);
    public int UnderexposedCount => _allPhotos.Count(p => p.IsUnderexposed);
    public int OverexposedCount => _allPhotos.Count(p => p.IsOverexposed);
    public int BurstCount => _allPhotos.Count(p => p.IsInBurst);

    public List<string> AvailableCameras => _allPhotos
        .Where(p => !string.IsNullOrEmpty(p.CameraModel))
        .Select(p => p.CameraModel!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c)
        .ToList();

    public event Action? StatsChanged;

    public async Task SavePhotoMetadataAsync(PhotoItem item)
    {
        try
        {
            await _database.SaveOrUpdatePhotosAsync([item]);
        }
        catch { }
    }

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

        // 0. Limpa arquivos fantasmas (AppleDouble ._* do macOS ou < 1KB)
        await _database.RemoveGhostFilesAsync(directoryPath);

        // 1. Obter dados já cacheados no SQLite
        var cachedData = await _database.GetCachedPhotoDataForDirectoryAsync(directoryPath);

        // 2. Enumerar arquivos suportados (ignorando ocultos, AppleDouble e < 1KB)
        var files = Directory.EnumerateFiles(directoryPath)
            .Where(RawPreviewExtractor.IsSupported)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith('.') || name.StartsWith("._", StringComparison.OrdinalIgnoreCase))
                    return false;
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Length < 1024) return false;
                    if ((fi.Attributes & FileAttributes.Hidden) != 0) return false;
                }
                catch { return false; }
                return true;
            })
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
                item.Rating = cacheInfo.Rating;
                item.ColorLabel = cacheInfo.Label;
                item.ThumbnailCachePath = cacheInfo.Thumb;
                item.Orientation = cacheInfo.Orient;
                item.Width = cacheInfo.Width;
                item.Height = cacheInfo.Height;
                item.CameraModel = cacheInfo.CameraModel;
                item.CameraMake = cacheInfo.CameraMake;
                item.LensModel = cacheInfo.LensModel;
                item.Iso = cacheInfo.Iso;
                item.FNumber = cacheInfo.FNumber;
                item.ExposureTime = cacheInfo.ExposureTime;
                item.FocalLength = cacheInfo.FocalLength;
                item.ExposureBias = cacheInfo.ExposureBias;
                item.FlashFired = cacheInfo.FlashFired;
                item.DateTaken = cacheInfo.DateTaken;
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

        // Agrupa rajadas inicialmente
        BurstStackingService.GroupBursts(_allPhotos);

        ApplyFilter(CurrentFilter);
        StatsChanged?.Invoke();

        // A extração priorizada e o cache de thumbnails são gerenciados de forma ágil pela ThumbnailLoaderQueue na camada Desktop

        return _allPhotos.Count;
    }

    public int RatingCount(int stars) => _allPhotos.Count(p => p.Rating == stars);

    public void SetCameraFilter(string? camera)
    {
        CurrentCameraFilter = camera;
        ApplyFilter(CurrentFilter);
    }

    public void SetFlashFilter(bool? flashFired)
    {
        CurrentFlashFilter = flashFired;
        ApplyFilter(CurrentFilter);
    }

    public void SetFormatFilter(string? format)
    {
        CurrentFormatFilter = (string.Equals(format, "ALL", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(format)) ? null : format;
        ApplyFilter(CurrentFilter);
    }

    public List<PhotoItem> PickBurstBest(PhotoItem item)
    {
        var affected = BurstStackingService.PickBestAndRejectRest(item, _allPhotos);
        StatsChanged?.Invoke();
        return affected;
    }

    public void ApplyFilter(PhotoFilterMode mode)
    {
        CurrentFilter = mode;
        FilteredPhotos.Clear();

        IEnumerable<PhotoItem> query = mode switch
        {
            PhotoFilterMode.GoodOnly => _allPhotos.Where(p => p.IsGoodQuality),
            PhotoFilterMode.BlurryOnly => _allPhotos.Where(p => p.IsBlurry),
            PhotoFilterMode.UnderexposedOnly => _allPhotos.Where(p => p.IsUnderexposed),
            PhotoFilterMode.OverexposedOnly => _allPhotos.Where(p => p.IsOverexposed),
            PhotoFilterMode.BurstStacksOnly => _allPhotos.Where(p => !p.IsInBurst || p.IsBurstLead),
            PhotoFilterMode.PickedOnly => _allPhotos.Where(p => p.IsPicked),
            PhotoFilterMode.DoubtOnly => _allPhotos.Where(p => p.IsDoubt),
            PhotoFilterMode.RejectedOnly => _allPhotos.Where(p => p.IsRejected),
            PhotoFilterMode.UnflaggedOnly => _allPhotos.Where(p => !p.IsPicked && !p.IsRejected && !p.IsDoubt && p.Rating == 0),
            PhotoFilterMode.RatedOnly => _allPhotos.Where(p => p.Rating > 0),
            PhotoFilterMode.Rating5 => _allPhotos.Where(p => p.Rating == 5),
            PhotoFilterMode.Rating4 => _allPhotos.Where(p => p.Rating == 4),
            PhotoFilterMode.Rating3 => _allPhotos.Where(p => p.Rating == 3),
            PhotoFilterMode.Rating2 => _allPhotos.Where(p => p.Rating == 2),
            PhotoFilterMode.Rating1 => _allPhotos.Where(p => p.Rating == 1),
            _ => _allPhotos.AsEnumerable()
        };

        if (!string.IsNullOrEmpty(CurrentCameraFilter))
        {
            query = query.Where(p => string.Equals(p.CameraModel, CurrentCameraFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (CurrentFlashFilter.HasValue)
        {
            query = query.Where(p => p.FlashFired == CurrentFlashFilter.Value);
        }

        if (!string.IsNullOrEmpty(CurrentFormatFilter))
        {
            query = query.Where(p => string.Equals(p.Extension, CurrentFormatFilter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var photo in query)
        {
            FilteredPhotos.Add(photo);
        }

        StatsChanged?.Invoke();
    }

    public async Task<int> SaveOnlySelectedXmpAsync(IEnumerable<PhotoItem>? targetItems = null, CancellationToken ct = default)
    {
        var selected = (targetItems ?? _allPhotos.Where(p => p.IsPicked)).Where(p => p.IsPicked).ToList();
        var unselected = _allPhotos.Where(p => !p.IsPicked && (p.Rating > 0 || !string.IsNullOrEmpty(p.ColorLabel) || p.HasXmp)).ToList();

        // 1. Grava metadados XMP apenas nas selecionadas
        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Rating == 0) item.Rating = 1;
            if (string.IsNullOrEmpty(item.ColorLabel)) item.ColorLabel = "Green";
            _xmpService.WriteMetadata(item.FilePath, item.Rating, item.ColorLabel);
            item.IsModified = false;
        }

        // 2. Limpa marcações de estrelas/cores de qualquer foto que não foi selecionada
        foreach (var item in unselected)
        {
            ct.ThrowIfCancellationRequested();
            if (_xmpService.XmpExists(item.FilePath))
            {
                _xmpService.WriteMetadata(item.FilePath, 0, null);
            }
            item.IsModified = false;
        }

        await _database.SaveOrUpdatePhotosAsync(selected.Concat(unselected));
        StatsChanged?.Invoke();
        return selected.Count;
    }

    public async Task<int> ExportSpecificPhotosAsync(IEnumerable<PhotoItem> itemsToExport, string destinationFolder, IProgress<(int current, int total)>? progress = null, CancellationToken ct = default)
    {
        var list = itemsToExport.ToList();
        if (list.Count == 0) return 0;

        Directory.CreateDirectory(destinationFolder);

        // Garante que o XMP de cada selecionada está salvo
        await SaveOnlySelectedXmpAsync(list, ct);

        int copied = 0;
        int total = list.Count;

        await Task.Run(() =>
        {
            foreach (var photo in list)
            {
                ct.ThrowIfCancellationRequested();

                string destPhoto = Path.Combine(destinationFolder, photo.FileName);
                File.Copy(photo.FilePath, destPhoto, overwrite: true);

                string xmpSource = photo.XmpPath;
                if (File.Exists(xmpSource))
                {
                    string destXmp = Path.Combine(destinationFolder, Path.GetFileName(xmpSource));
                    File.Copy(xmpSource, destXmp, overwrite: true);
                }

                copied++;
                progress?.Report((copied, total));
            }
        }, ct);

        return copied;
    }

    public async Task<int> ExportSelectedPhotosAsync(string destinationFolder, IProgress<(int current, int total)>? progress = null, CancellationToken ct = default)
    {
        return await ExportSpecificPhotosAsync(_allPhotos.Where(p => p.IsPicked), destinationFolder, progress, ct);
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

    public void ToggleDoubt(PhotoItem item)
    {
        item.IsDoubt = !item.IsDoubt;
        item.IsModified = true;
        StatsChanged?.Invoke();
    }

    public bool DeletePhoto(PhotoItem photo)
    {
        bool removed = _allPhotos.Remove(photo);
        FilteredPhotos.Remove(photo);
        BurstStackingService.GroupBursts(_allPhotos);
        StatsChanged?.Invoke();
        return removed;
    }

    public async Task<bool> DeletePhotoAsync(PhotoItem photo)
    {
        bool removed = DeletePhoto(photo);
        try
        {
            await _database.DeletePhotoAsync(photo.FilePath);
        }
        catch { }
        return removed;
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
                if (!string.IsNullOrEmpty(res.CameraModel)) item.CameraModel = res.CameraModel;
                if (!string.IsNullOrEmpty(res.CameraMake)) item.CameraMake = res.CameraMake;
                if (!string.IsNullOrEmpty(res.LensModel)) item.LensModel = res.LensModel;
                if (res.Iso.HasValue) item.Iso = res.Iso;
                if (res.FNumber.HasValue) item.FNumber = res.FNumber;
                if (res.ExposureTime.HasValue) item.ExposureTime = res.ExposureTime;
                if (res.FocalLength.HasValue) item.FocalLength = res.FocalLength;
                if (res.ExposureBias.HasValue) item.ExposureBias = res.ExposureBias;
                if (res.FlashFired.HasValue) item.FlashFired = res.FlashFired;
                if (res.DateTaken.HasValue) item.DateTaken ??= res.DateTaken;

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

        // Limita a concorrência a 2 workers simultâneos para não travar cartões SD e pendrives USB
        using var semaphore = new SemaphoreSlim(2);
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

        // Recalcula agrupamento de rajadas com os metadados refinados
        BurstStackingService.GroupBursts(_allPhotos);
        StatsChanged?.Invoke();

        // Salva estado de thumbnails atualizado no banco
        try
        {
            await _database.SaveOrUpdatePhotosAsync(_allPhotos);
        }
        catch { }
    }
}
