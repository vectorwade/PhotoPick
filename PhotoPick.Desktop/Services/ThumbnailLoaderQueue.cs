using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PhotoPick.Core.Services;
using PhotoPick.Desktop.Helpers;
using PhotoPick.Desktop.ViewModels;

namespace PhotoPick.Desktop.Services;

public class ThumbnailLoaderQueue
{
    private readonly CullingSession _session;
    private readonly ConcurrentQueue<PhotoViewModel> _queue = new();
    private readonly HashSet<string> _inQueue = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task[]? _workers;

    public ThumbnailLoaderQueue(CullingSession session)
    {
        _session = session;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        int workerCount = 2; // 2 workers max: evita saturar a fila I/O de pendrives/cartões SD USB
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

    public void Enqueue(PhotoViewModel item)
    {
        if (item.Thumbnail != null) return;

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
                try { await Task.Delay(50, ct); } catch { break; }
                continue;
            }

            try
            {
                if (item.Thumbnail == null)
                {
                    string? thumbPath = await _session.EnsureThumbnailAsync(item.Model, ct);
                    if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
                    {
                        var bmp = ImageHelper.LoadBitmapFromFile(thumbPath, item.Orientation, decodePixelWidth: 320);
                        if (bmp != null)
                        {
                            ImageQualityHelper.AnalyzeAndApply(bmp, item.Model);

                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                item.Thumbnail = bmp;
                            });
                        }
                    }
                }
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
