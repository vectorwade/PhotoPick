using System;

namespace PhotoPick.Core.Models;

public class RawPreviewResult
{
    public bool Success { get; init; }
    public byte[]? JpegBytes { get; init; }
    public int Orientation { get; init; } = 1;
    public int Width { get; init; }
    public int Height { get; init; }
    public double ElapsedMilliseconds { get; init; }
    public string? ErrorMessage { get; init; }

    public static RawPreviewResult Fail(string error, double elapsedMs = 0) => new()
    {
        Success = false,
        ErrorMessage = error,
        ElapsedMilliseconds = elapsedMs
    };

    public static RawPreviewResult Ok(byte[] jpegBytes, int orientation = 1, int width = 0, int height = 0, double elapsedMs = 0) => new()
    {
        Success = true,
        JpegBytes = jpegBytes,
        Orientation = orientation,
        Width = width,
        Height = height,
        ElapsedMilliseconds = elapsedMs
    };
}
