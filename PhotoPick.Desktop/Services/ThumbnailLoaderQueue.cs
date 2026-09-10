using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using PhotoPick.Core.Models;
using PhotoPick.Core.Services;
using PhotoPick.Desktop.Helpers;
using PhotoPick.Desktop.ViewModels;

namespace PhotoPick.Desktop.Services;

public class ThumbnailLoaderQueue
{
    private readonly CullingSession _session;
    private readonly IRawPreviewExtractor _extractor;
    private readonly IThumbnailCacheService _cacheService;

    private readonly ConcurrentQueue<PhotoViewModel> _queue = new();
    private readonly HashSet<string> _inQueue = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task[]? _workers;

    public static readonly ConcurrentDictionary<string, BitmapSource> MemoryCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCached(string filePath, out BitmapSource? bmp)
    {
        return MemoryCache.TryGetValue(filePath, out bmp);
    }

    public static void Store(string filePath, BitmapSource bmp)
    {
        MemoryCache[filePath] = bmp;
    }

    public ThumbnailLoaderQueue(CullingSession session, IRawPreviewExtractor? extractor = null, IThumbnailCacheService? cacheService = null)
    {
        _session = session;
        _extractor = extractor ?? session.Extractor;
        _cacheService = cacheService ?? session.CacheService;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        int workerCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        _workers = new Task[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            _workers[i] = Task.Run(() => WorkerLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_lock)
        {
            _inQueue.Clear();
            while (_queue.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// Carrega a miniatura diretamente e de forma ultrarrápida da memória (JPEG embutido ou cache de disco).
    /// Não bloqueia a thread de renderização e atualiza o ViewModel imediatamente na UI.
    /// </summary>
    public async Task<BitmapSource?> LoadThumbnailDirectAsync(PhotoViewModel item, CancellationToken ct = default)
    {
        if (item.Thumbnail != null) return item.Thumbnail as BitmapSource;

        if (MemoryCache.TryGetValue(item.FilePath, out var cached) && cached != null)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => item.Thumbnail = cached);
            return cached;
        }

        try
        {
            // 1. Verifica se já existe cache em disco de sessões anteriores
            if (!string.IsNullOrEmpty(item.Model.ThumbnailCachePath) && File.Exists(item.Model.ThumbnailCachePath))
            {
                var diskBmp = ImageHelper.LoadBitmapFromFile(item.Model.ThumbnailCachePath, item.Orientation, decodePixelWidth: 320);
                if (diskBmp != null)
                {
                    MemoryCache[item.FilePath] = diskBmp;
                    await Application.Current.Dispatcher.InvokeAsync(() => item.Thumbnail = diskBmp);
                    return diskBmp;
                }
            }

            // 2. Extração instantânea do preview JPEG embutido no cabeçalho RAW/DNG
            var res = await _extractor.ExtractPreviewAsync(item.FilePath, ct);
            if (res.Success && res.JpegBytes != null)
            {
                // Decodifica diretamente na resolução de miniatura (320px) para consumo mínimo de RAM
                var bmp = ImageHelper.LoadBitmapFromBytes(res.JpegBytes, res.Orientation, decodePixelWidth: 320);
                if (bmp != null)
                {
                    MemoryCache[item.FilePath] = bmp;

                    // Atualiza metadados do modelo
                    item.Model.Orientation = res.Orientation;
                    item.Model.Width = res.Width;
                    item.Model.Height = res.Height;
                    if (!string.IsNullOrEmpty(res.CameraModel)) item.Model.CameraModel = res.CameraModel;
                    if (!string.IsNullOrEmpty(res.CameraMake)) item.Model.CameraMake = res.CameraMake;
                    if (!string.IsNullOrEmpty(res.LensModel)) item.Model.LensModel = res.LensModel;
                    if (res.Iso.HasValue) item.Model.Iso = res.Iso;
                    if (res.FNumber.HasValue) item.Model.FNumber = res.FNumber;
                    if (res.ExposureTime.HasValue) item.Model.ExposureTime = res.ExposureTime;
                    if (res.FocalLength.HasValue) item.Model.FocalLength = res.FocalLength;
                    if (res.ExposureBias.HasValue) item.Model.ExposureBias = res.ExposureBias;
                    if (res.FlashFired.HasValue) item.Model.FlashFired = res.FlashFired;
                    if (res.DateTaken.HasValue) item.Model.DateTaken ??= res.DateTaken;

                    // Publica na UI imediatamente
                    await Application.Current.Dispatcher.InvokeAsync(() => item.Thumbnail = bmp);

                    // Em background desacoplado: persiste no cache de disco, analisa qualidade e atualiza o banco
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            ImageQualityHelper.AnalyzeAndApply(bmp, item.Model);
                            string saved = await _cacheService.SaveThumbnailAsync(item.FilePath, res.JpegBytes);
                            item.Model.ThumbnailCachePath = saved;
                            await _session.SavePhotoMetadataAsync(item.Model);
                        }
                        catch { }
                    }, CancellationToken.None);

                    return bmp;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Dispara carregamento imediato paralelo para o lote visível inicial (sem enfileiramento lento).
    /// </summary>
    public void PrioritizeVisible(IEnumerable<PhotoViewModel> visibleItems)
    {
        var list = visibleItems.Take(36).Where(i => i.Thumbnail == null).ToList();
        if (list.Count == 0) return;

        _ = Task.Run(async () =>
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 4, 8),
                CancellationToken = _cts?.Token ?? CancellationToken.None
            };

            await Parallel.ForEachAsync(list, options, async (item, token) =>
            {
                await LoadThumbnailDirectAsync(item, token);
            });
        });
    }

    /// <summary>
    /// Prioriza os primeiros 36 itens imediatamente e enfileira o restante para o loop de background.
    /// </summary>
    public void PrioritizeAndEnqueue(IEnumerable<PhotoViewModel> allItems)
    {
        var list = allItems.ToList();
        var visible = list.Take(36);
        var remaining = list.Skip(36);

        PrioritizeVisible(visible);
        EnqueueBatch(remaining);
    }

    public void Enqueue(PhotoViewModel item)
    {
        if (item.Thumbnail != null) return;

        if (MemoryCache.TryGetValue(item.FilePath, out var cached) && cached != null)
        {
            item.Thumbnail = cached;
            return;
        }

        lock (_lock)
        {
            if (_inQueue.Add(item.FilePath))
            {
                _queue.Enqueue(item);
            }
        }
    }

    public void EnqueueBatch(IEnumerable<PhotoViewModel> items)
    {
        foreach (var item in items)
        {
            Enqueue(item);
        }
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PhotoViewModel? item = null;
            lock (_lock)
            {
                if (_queue.TryDequeue(out var dequeued))
                {
                    item = dequeued;
                }
            }

            if (item == null)
            {
                try { await Task.Delay(40, ct); } catch { break; }
                continue;
            }

            try
            {
                await LoadThumbnailDirectAsync(item, ct);
            }
            catch { }
            finally
            {
                lock (_lock)
                {
                    _inQueue.Remove(item.FilePath);
                }
            }
        }
    }
}
