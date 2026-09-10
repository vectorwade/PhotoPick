using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PhotoPick.Core.Models;

namespace PhotoPick.Core.Services;

public class CatalogDatabase
{
    private readonly string _dbPath;

    public CatalogDatabase(string? customDbPath = null)
    {
        string dir = customDbPath != null
            ? Path.GetDirectoryName(customDbPath)!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoPick");

        Directory.CreateDirectory(dir);
        _dbPath = customDbPath ?? Path.Combine(dir, "catalog.db");
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = CreateConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Photos (
                FilePath TEXT PRIMARY KEY,
                FileName TEXT NOT NULL,
                DirectoryPath TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                Orientation INTEGER NOT NULL DEFAULT 1,
                Width INTEGER NOT NULL DEFAULT 0,
                Height INTEGER NOT NULL DEFAULT 0,
                Rating INTEGER NOT NULL DEFAULT 0,
                ColorLabel TEXT,
                ThumbnailCachePath TEXT,
                LastScannedTicks INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Photos_Directory ON Photos (DirectoryPath);
        """;
        cmd.ExecuteNonQuery();
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_dbPath}");
    }

    public async Task SaveOrUpdatePhotosAsync(IEnumerable<PhotoItem> photos)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var trans = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = """
            INSERT INTO Photos (FilePath, FileName, DirectoryPath, FileSize, Orientation, Width, Height, Rating, ColorLabel, ThumbnailCachePath, LastScannedTicks)
            VALUES ($path, $name, $dir, $size, $orient, $width, $height, $rating, $label, $thumb, $scanned)
            ON CONFLICT(FilePath) DO UPDATE SET
                Rating = excluded.Rating,
                ColorLabel = excluded.ColorLabel,
                ThumbnailCachePath = excluded.ThumbnailCachePath,
                Orientation = excluded.Orientation,
                Width = excluded.Width,
                Height = excluded.Height,
                LastScannedTicks = excluded.LastScannedTicks;
        """;

        var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pDir = cmd.Parameters.Add("$dir", SqliteType.Text);
        var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
        var pOrient = cmd.Parameters.Add("$orient", SqliteType.Integer);
        var pWidth = cmd.Parameters.Add("$width", SqliteType.Integer);
        var pHeight = cmd.Parameters.Add("$height", SqliteType.Integer);
        var pRating = cmd.Parameters.Add("$rating", SqliteType.Integer);
        var pLabel = cmd.Parameters.Add("$label", SqliteType.Text);
        var pThumb = cmd.Parameters.Add("$thumb", SqliteType.Text);
        var pScanned = cmd.Parameters.Add("$scanned", SqliteType.Integer);

        long nowTicks = DateTime.UtcNow.Ticks;

        foreach (var p in photos)
        {
            pPath.Value = p.FilePath;
            pName.Value = p.FileName;
            pDir.Value = p.DirectoryPath;
            pSize.Value = p.FileSize;
            pOrient.Value = p.Orientation;
            pWidth.Value = p.Width;
            pHeight.Value = p.Height;
            pRating.Value = p.Rating;
            pLabel.Value = (object?)p.ColorLabel ?? DBNull.Value;
            pThumb.Value = (object?)p.ThumbnailCachePath ?? DBNull.Value;
            pScanned.Value = nowTicks;

            await cmd.ExecuteNonQueryAsync();
        }

        await trans.CommitAsync();
    }

    public async Task<Dictionary<string, (int rating, string? label, string? thumb, int orient, int width, int height)>> GetCachedPhotoDataForDirectoryAsync(string directoryPath)
    {
        var result = new Dictionary<string, (int rating, string? label, string? thumb, int orient, int width, int height)>(StringComparer.OrdinalIgnoreCase);

        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FilePath, Rating, ColorLabel, ThumbnailCachePath, Orientation, Width, Height FROM Photos WHERE DirectoryPath = $dir";
        cmd.Parameters.AddWithValue("$dir", directoryPath);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string path = reader.GetString(0);
            int rating = reader.GetInt32(1);
            string? label = reader.IsDBNull(2) ? null : reader.GetString(2);
            string? thumb = reader.IsDBNull(3) ? null : reader.GetString(3);
            int orient = reader.GetInt32(4);
            int width = reader.GetInt32(5);
            int height = reader.GetInt32(6);

            result[path] = (rating, label, thumb, orient, width, height);
        }

        return result;
    }

    public async Task DeletePhotoAsync(string filePath)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Photos WHERE FilePath = $path";
        cmd.Parameters.AddWithValue("$path", filePath);
        await cmd.ExecuteNonQueryAsync();
    }
}
