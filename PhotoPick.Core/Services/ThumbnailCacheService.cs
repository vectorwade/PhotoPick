using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PhotoPick.Core.Services;

public interface IThumbnailCacheService
{
    string? GetCachedThumbnailPath(string sourceFilePath);
    Task<string> SaveThumbnailAsync(string sourceFilePath, byte[] jpegBytes);
    void ClearCache();
}

public class ThumbnailCacheService : IThumbnailCacheService
{
    private readonly string _cacheDir;

    public ThumbnailCacheService(string? customCacheDir = null)
    {
        _cacheDir = customCacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoPick",
            "Thumbnails"
        );

        Directory.CreateDirectory(_cacheDir);
    }

    public string? GetCachedThumbnailPath(string sourceFilePath)
    {
        string key = ComputeCacheKey(sourceFilePath);
        string path = Path.Combine(_cacheDir, $"{key}.jpg");
        return File.Exists(path) ? path : null;
    }

    public async Task<string> SaveThumbnailAsync(string sourceFilePath, byte[] jpegBytes)
    {
        string key = ComputeCacheKey(sourceFilePath);
        string path = Path.Combine(_cacheDir, $"{key}.jpg");

        if (!File.Exists(path))
        {
            string tempPath = path + $".tmp.{Guid.NewGuid():N}";
            await File.WriteAllBytesAsync(tempPath, jpegBytes);
            try
            {
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        return path;
    }

    public void ClearCache()
    {
        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.GetFiles(_cacheDir, "*.jpg"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private static string ComputeCacheKey(string sourceFilePath)
    {
        try
        {
            var fi = new FileInfo(sourceFilePath);
            string raw = $"{fi.FullName.ToLowerInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash)[..24];
        }
        catch
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceFilePath.ToLowerInvariant()));
            return Convert.ToHexString(hash)[..24];
        }
    }
}
