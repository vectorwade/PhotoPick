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

    public string? CameraModel { get; init; }
    public string? CameraMake { get; init; }
    public bool? FlashFired { get; init; }
    public DateTime? DateTaken { get; init; }

    public string? LensModel { get; init; }
    public int? Iso { get; init; }
    public double? FNumber { get; init; }
    public double? ExposureTime { get; init; }
    public double? FocalLength { get; init; }
    public double? ExposureBias { get; init; }
    public int? FocalLength35mm { get; init; }
    public double? MaxAperture { get; init; }
    public string? MeteringMode { get; init; }
    public string? ExposureProgram { get; init; }
    public string? ExposureMode { get; init; }
    public string? WhiteBalance { get; init; }
    public string? Software { get; init; }
    public string? SerialNumber { get; init; }

    public static RawPreviewResult Fail(string error, double elapsedMs = 0) => new()
    {
        Success = false,
        ErrorMessage = error,
        ElapsedMilliseconds = elapsedMs
    };

    public static RawPreviewResult Ok(
        byte[] jpegBytes, 
        int orientation = 1, 
        int width = 0, 
        int height = 0, 
        double elapsedMs = 0,
        string? cameraModel = null,
        string? cameraMake = null,
        bool? flashFired = null,
        DateTime? dateTaken = null,
        string? lensModel = null,
        int? iso = null,
        double? fNumber = null,
        double? exposureTime = null,
        double? focalLength = null,
        double? exposureBias = null,
        int? focalLength35mm = null,
        double? maxAperture = null,
        string? meteringMode = null,
        string? exposureProgram = null,
        string? exposureMode = null,
        string? whiteBalance = null,
        string? software = null,
        string? serialNumber = null) => new()
    {
        Success = true,
        JpegBytes = jpegBytes,
        Orientation = orientation,
        Width = width,
        Height = height,
        ElapsedMilliseconds = elapsedMs,
        CameraModel = cameraModel,
        CameraMake = cameraMake,
        FlashFired = flashFired,
        DateTaken = dateTaken,
        LensModel = lensModel,
        Iso = iso,
        FNumber = fNumber,
        ExposureTime = exposureTime,
        FocalLength = focalLength,
        ExposureBias = exposureBias,
        FocalLength35mm = focalLength35mm,
        MaxAperture = maxAperture,
        MeteringMode = meteringMode,
        ExposureProgram = exposureProgram,
        ExposureMode = exposureMode,
        WhiteBalance = whiteBalance,
        Software = software,
        SerialNumber = serialNumber
    };
}
