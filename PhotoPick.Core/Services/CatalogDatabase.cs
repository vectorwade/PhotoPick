using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PhotoPick.Core.Models;

namespace PhotoPick.Core.Services;

public record CachedPhotoMetadata(
    int Rating,
    string? Label,
    string? Thumb,
    int Orient,
    int Width,
    int Height,
    string? CameraModel = null,
    string? CameraMake = null,
    string? LensModel = null,
    int? Iso = null,
    double? FNumber = null,
    double? ExposureTime = null,
    double? FocalLength = null,
    double? ExposureBias = null,
    bool? FlashFired = null,
    DateTime? DateTaken = null);

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
                LastScannedTicks INTEGER NOT NULL,
                CameraModel TEXT,
                CameraMake TEXT,
                LensModel TEXT,
                Iso INTEGER,
                FNumber REAL,
                ExposureTime REAL,
                FocalLength REAL,
                ExposureBias REAL,
                FlashFired INTEGER,
                DateTaken TEXT
            );

            CREATE INDEX IF NOT EXISTS IX_Photos_Directory ON Photos (DirectoryPath);
        """;
        cmd.ExecuteNonQuery();

        // Migração de colunas para bancos existentes
        string[] migrations = [
            "ALTER TABLE Photos ADD COLUMN CameraModel TEXT;",
            "ALTER TABLE Photos ADD COLUMN CameraMake TEXT;",
            "ALTER TABLE Photos ADD COLUMN LensModel TEXT;",
            "ALTER TABLE Photos ADD COLUMN Iso INTEGER;",
            "ALTER TABLE Photos ADD COLUMN FNumber REAL;",
            "ALTER TABLE Photos ADD COLUMN ExposureTime REAL;",
            "ALTER TABLE Photos ADD COLUMN FocalLength REAL;",
            "ALTER TABLE Photos ADD COLUMN ExposureBias REAL;",
            "ALTER TABLE Photos ADD COLUMN FlashFired INTEGER;",
            "ALTER TABLE Photos ADD COLUMN DateTaken TEXT;"
        ];

        foreach (var m in migrations)
        {
            try
            {
                using var mCmd = conn.CreateCommand();
                mCmd.CommandText = m;
                mCmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_dbPath}");
    }

    public async Task RemoveGhostFilesAsync(string directoryPath)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Photos WHERE DirectoryPath = $dir AND (FileName LIKE '._%' OR FileName LIKE '.%' OR FileSize < 1024)";
            cmd.Parameters.AddWithValue("$dir", directoryPath);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    public async Task SaveOrUpdatePhotosAsync(IEnumerable<PhotoItem> photos)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        using var trans = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = """
            INSERT INTO Photos (
                FilePath, FileName, DirectoryPath, FileSize, Orientation, Width, Height,
                Rating, ColorLabel, ThumbnailCachePath, LastScannedTicks,
                CameraModel, CameraMake, LensModel, Iso, FNumber, ExposureTime, FocalLength, ExposureBias, FlashFired, DateTaken
            )
            VALUES (
                $path, $name, $dir, $size, $orient, $width, $height,
                $rating, $label, $thumb, $scanned,
                $camModel, $camMake, $lens, $iso, $fnum, $exp, $focal, $bias, $flash, $dateTaken
            )
            ON CONFLICT(FilePath) DO UPDATE SET
                Rating = excluded.Rating,
                ColorLabel = excluded.ColorLabel,
                ThumbnailCachePath = excluded.ThumbnailCachePath,
                Orientation = excluded.Orientation,
                Width = excluded.Width,
                Height = excluded.Height,
                LastScannedTicks = excluded.LastScannedTicks,
                CameraModel = coalesce(excluded.CameraModel, Photos.CameraModel),
                CameraMake = coalesce(excluded.CameraMake, Photos.CameraMake),
                LensModel = coalesce(excluded.LensModel, Photos.LensModel),
                Iso = coalesce(excluded.Iso, Photos.Iso),
                FNumber = coalesce(excluded.FNumber, Photos.FNumber),
                ExposureTime = coalesce(excluded.ExposureTime, Photos.ExposureTime),
                FocalLength = coalesce(excluded.FocalLength, Photos.FocalLength),
                ExposureBias = coalesce(excluded.ExposureBias, Photos.ExposureBias),
                FlashFired = coalesce(excluded.FlashFired, Photos.FlashFired),
                DateTaken = coalesce(excluded.DateTaken, Photos.DateTaken);
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

        var pCamModel = cmd.Parameters.Add("$camModel", SqliteType.Text);
        var pCamMake = cmd.Parameters.Add("$camMake", SqliteType.Text);
        var pLens = cmd.Parameters.Add("$lens", SqliteType.Text);
        var pIso = cmd.Parameters.Add("$iso", SqliteType.Integer);
        var pFnum = cmd.Parameters.Add("$fnum", SqliteType.Real);
        var pExp = cmd.Parameters.Add("$exp", SqliteType.Real);
        var pFocal = cmd.Parameters.Add("$focal", SqliteType.Real);
        var pBias = cmd.Parameters.Add("$bias", SqliteType.Real);
        var pFlash = cmd.Parameters.Add("$flash", SqliteType.Integer);
        var pDateTaken = cmd.Parameters.Add("$dateTaken", SqliteType.Text);

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

            pCamModel.Value = (object?)p.CameraModel ?? DBNull.Value;
            pCamMake.Value = (object?)p.CameraMake ?? DBNull.Value;
            pLens.Value = (object?)p.LensModel ?? DBNull.Value;
            pIso.Value = (object?)p.Iso ?? DBNull.Value;
            pFnum.Value = (object?)p.FNumber ?? DBNull.Value;
            pExp.Value = (object?)p.ExposureTime ?? DBNull.Value;
            pFocal.Value = (object?)p.FocalLength ?? DBNull.Value;
            pBias.Value = (object?)p.ExposureBias ?? DBNull.Value;
            pFlash.Value = p.FlashFired.HasValue ? (p.FlashFired.Value ? 1 : 0) : DBNull.Value;
            pDateTaken.Value = p.DateTaken.HasValue ? p.DateTaken.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        await trans.CommitAsync();
    }

    public async Task<Dictionary<string, CachedPhotoMetadata>> GetCachedPhotoDataForDirectoryAsync(string directoryPath)
    {
        var result = new Dictionary<string, CachedPhotoMetadata>(StringComparer.OrdinalIgnoreCase);

        using var conn = CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT FilePath, Rating, ColorLabel, ThumbnailCachePath, Orientation, Width, Height,
                   CameraModel, CameraMake, LensModel, Iso, FNumber, ExposureTime, FocalLength, ExposureBias, FlashFired, DateTaken
            FROM Photos WHERE DirectoryPath = $dir
        """;
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

            string? camModel = reader.IsDBNull(7) ? null : reader.GetString(7);
            string? camMake = reader.IsDBNull(8) ? null : reader.GetString(8);
            string? lens = reader.IsDBNull(9) ? null : reader.GetString(9);
            int? iso = reader.IsDBNull(10) ? null : reader.GetInt32(10);
            double? fnum = reader.IsDBNull(11) ? null : reader.GetDouble(11);
            double? exp = reader.IsDBNull(12) ? null : reader.GetDouble(12);
            double? focal = reader.IsDBNull(13) ? null : reader.GetDouble(13);
            double? bias = reader.IsDBNull(14) ? null : reader.GetDouble(14);
            bool? flash = reader.IsDBNull(15) ? null : (reader.GetInt32(15) == 1);
            DateTime? dateTaken = null;
            if (!reader.IsDBNull(16) && DateTime.TryParse(reader.GetString(16), out var dt))
            {
                dateTaken = dt;
            }

            result[path] = new CachedPhotoMetadata(
                rating, label, thumb, orient, width, height,
                camModel, camMake, lens, iso, fnum, exp, focal, bias, flash, dateTaken);
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
