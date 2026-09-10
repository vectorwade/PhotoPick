using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhotoPick.Core.Models;

namespace PhotoPick.Core.Services;

public interface IRawPreviewExtractor
{
    RawPreviewResult ExtractPreview(string filePath);
    Task<RawPreviewResult> ExtractPreviewAsync(string filePath, CancellationToken ct = default);
}

public class RawPreviewExtractor : IRawPreviewExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".arw", ".nef", ".dng", ".raf", ".orf", ".pef", ".rw2", ".jpg", ".jpeg"
    };

    public static bool IsSupported(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string fileName = Path.GetFileName(filePath);
        if (fileName.StartsWith("._", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.StartsWith(".", StringComparison.OrdinalIgnoreCase)) return false;

        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public RawPreviewResult ExtractPreview(string filePath)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(filePath))
        {
            return RawPreviewResult.Fail($"Arquivo não encontrado: {filePath}");
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            // Para JPEGs comuns:
            if (ext is ".jpg" or ".jpeg")
            {
                return ExtractFromJpegFile(filePath, sw);
            }

            // Para Canon CR3 (ISOBMFF):
            if (ext == ".cr3")
            {
                var cr3Result = ExtractFromCr3(filePath, sw);
                if (cr3Result.Success) return cr3Result;
            }

            // Para Fujifilm RAF:
            if (ext == ".raf")
            {
                var rafResult = ExtractFromRaf(filePath, sw);
                if (rafResult.Success) return rafResult;
            }

            // Para formatos baseados em TIFF (.CR2, .ARW, .NEF, .DNG, .ORF, .PEF, .RW2):
            var tiffResult = ExtractFromTiff(filePath, sw);
            if (tiffResult.Success) return tiffResult;

            // Fallback genérico: varredura rápida de cabeçalho por JPEG embutido
            var fallbackResult = ScanForEmbeddedJpeg(filePath, sw);
            if (fallbackResult.Success) return fallbackResult;

            sw.Stop();
            return RawPreviewResult.Fail($"Nenhum preview embutido suportado encontrado em {ext}.", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return RawPreviewResult.Fail($"Erro ao extrair preview: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    public Task<RawPreviewResult> ExtractPreviewAsync(string filePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return ExtractPreview(filePath);
        }, ct);
    }

    #region Normalização de Câmeras e Drones DJI

    public static string? NormalizeCameraModel(string? make, string? model)
    {
        if (string.IsNullOrWhiteSpace(make) && string.IsNullOrWhiteSpace(model)) return null;

        string cleanMake = (make ?? "").Trim();
        string cleanModel = (model ?? "").Trim();

        // Detecção e mapeamento de drones DJI
        bool isDji = cleanMake.Contains("DJI", StringComparison.OrdinalIgnoreCase) ||
                     cleanModel.StartsWith("FC", StringComparison.OrdinalIgnoreCase) ||
                     cleanModel.Contains("DJI", StringComparison.OrdinalIgnoreCase);

        if (isDji)
        {
            string rawCode = cleanModel;
            if (rawCode.StartsWith("DJI ", StringComparison.OrdinalIgnoreCase))
                rawCode = rawCode.Substring(4).Trim();
            else if (rawCode.StartsWith("DJI", StringComparison.OrdinalIgnoreCase))
                rawCode = rawCode.Substring(3).Trim();

            string djiName = rawCode.ToUpperInvariant() switch
            {
                "FC9313" or "FC-9313" => "DJI Neo",
                "FC8482" or "FC-8482" => "DJI Avata 2",
                "FC3592" or "FC-3592" => "DJI Mini 3",
                "FC4311" or "FC-4311" => "DJI Air 3",
                "L2P-20C" or "L2P_20C" or "L2P20C" => "DJI Mavic 3 Pro",
                "L1D-20C" or "L1D_20C" or "L1D20C" => "DJI Mavic 2 Pro",
                "L1P-20C" or "L1P_20C" or "L1P20C" => "DJI Mavic 2 Zoom",
                "FC3582" or "FC-3582" => "DJI Mini 3 Pro",
                "FC3411" or "FC-3411" => "DJI Air 2S",
                "FC8284" or "FC-8284" => "DJI Mini 4 Pro",
                "FC2403" or "FC-2403" => "DJI Mini 2",
                "FC3170" or "FC-3170" => "DJI Mavic Air 2",
                "FC220" or "FC-220" => "DJI Mavic Pro",
                "FC6310" or "FC-6310" => "DJI Phantom 4 Pro",
                "L2D-20C" or "L2D_20C" or "L2D20C" => "DJI Mavic 3 (Hasselblad)",
                "FC300X" or "FC-300X" => "DJI Phantom 3 Pro",
                "FC300S" or "FC-300S" => "DJI Phantom 3 Adv",
                _ => cleanModel.StartsWith("DJI", StringComparison.OrdinalIgnoreCase)
                    ? cleanModel
                    : (cleanModel.Length > 0 ? $"DJI {cleanModel}" : "DJI Drone")
            };
            return djiName;
        }

        if (!string.IsNullOrEmpty(cleanMake) && !string.IsNullOrEmpty(cleanModel))
        {
            if (cleanModel.StartsWith(cleanMake, StringComparison.OrdinalIgnoreCase))
            {
                return cleanModel;
            }
            return $"{cleanMake} {cleanModel}";
        }

        return !string.IsNullOrEmpty(cleanModel) ? cleanModel : cleanMake;
    }

    private static string? ReadTiffString(FileStream fs, uint valOffset, uint count, ReadOnlySpan<byte> entryOffsetBytes, bool isLittleEndian)
    {
        if (count == 0 || count > 1024) return null;
        byte[] strBuf = new byte[count];
        if (count <= 4)
        {
            entryOffsetBytes.Slice(0, (int)count).CopyTo(strBuf);
        }
        else
        {
            if (valOffset >= fs.Length) return null;
            long saved = fs.Position;
            fs.Seek(valOffset, SeekOrigin.Begin);
            int read = fs.Read(strBuf, 0, (int)count);
            fs.Seek(saved, SeekOrigin.Begin);
            if (read <= 0) return null;
        }
        int len = Array.IndexOf(strBuf, (byte)0);
        if (len < 0) len = strBuf.Length;
        string result = Encoding.ASCII.GetString(strBuf, 0, len).Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static double? ReadTiffRational(FileStream fs, uint valOffset, bool isLittleEndian)
    {
        if (valOffset >= fs.Length - 8) return null;
        long saved = fs.Position;
        fs.Seek(valOffset, SeekOrigin.Begin);
        Span<byte> buf = stackalloc byte[8];
        if (fs.Read(buf) < 8) { fs.Seek(saved, SeekOrigin.Begin); return null; }
        fs.Seek(saved, SeekOrigin.Begin);

        uint num = isLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(buf[0..4]) : BinaryPrimitives.ReadUInt32BigEndian(buf[0..4]);
        uint den = isLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(buf[4..8]) : BinaryPrimitives.ReadUInt32BigEndian(buf[4..8]);
        if (den == 0) return null;
        return (double)num / den;
    }

    private static double? ReadTiffSRational(FileStream fs, uint valOffset, bool isLittleEndian)
    {
        if (valOffset >= fs.Length - 8) return null;
        long saved = fs.Position;
        fs.Seek(valOffset, SeekOrigin.Begin);
        Span<byte> buf = stackalloc byte[8];
        if (fs.Read(buf) < 8) { fs.Seek(saved, SeekOrigin.Begin); return null; }
        fs.Seek(saved, SeekOrigin.Begin);

        int num = isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(buf[0..4]) : BinaryPrimitives.ReadInt32BigEndian(buf[0..4]);
        int den = isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(buf[4..8]) : BinaryPrimitives.ReadInt32BigEndian(buf[4..8]);
        if (den == 0) return null;
        return (double)num / den;
    }

    #endregion

    #region JPEG Direto

    private static RawPreviewResult ExtractFromJpegFile(string filePath, Stopwatch sw)
    {
        var bytes = File.ReadAllBytes(filePath);
        var (width, height, orientation, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias) = ParseJpegInfo(bytes);
        sw.Stop();
        string? normalized = NormalizeCameraModel(make, model);
        return RawPreviewResult.Ok(bytes, orientation, width, height, sw.Elapsed.TotalMilliseconds, normalized, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);
    }

    #endregion

    #region TIFF-based (CR2, ARW, NEF, DNG, ORF, PEF)

    private static RawPreviewResult ExtractFromTiff(string filePath, Stopwatch sw)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        if (fs.Length < 16) return RawPreviewResult.Fail("Arquivo muito pequeno para ser TIFF.");

        Span<byte> header = stackalloc byte[8];
        if (fs.Read(header) < 8) return RawPreviewResult.Fail("Não foi possível ler o cabeçalho TIFF.");

        bool isLittleEndian;
        if (header[0] == 0x49 && header[1] == 0x49) // "II"
        {
            isLittleEndian = true;
        }
        else if (header[0] == 0x4D && header[1] == 0x4D) // "MM"
        {
            isLittleEndian = false;
        }
        else
        {
            return RawPreviewResult.Fail("Assinatura TIFF inválida.");
        }

        ushort magic = isLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(header[2..4])
            : BinaryPrimitives.ReadUInt16BigEndian(header[2..4]);

        if (magic != 42 && magic != 0x4F52 && magic != 0x0055) // 42 padrão, 0x4F52 ORF, 0x0055 RW2
        {
            return RawPreviewResult.Fail($"Número mágico TIFF inválido: {magic}");
        }

        uint firstIfdOffset = isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(header[4..8])
            : BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);

        var candidates = new List<(long offset, long length)>();
        int orientation = 1;
        string? tiffMake = null;
        string? tiffModel = null;
        string? tiffLens = null;
        int? tiffIso = null;
        double? tiffFNumber = null;
        double? tiffExposure = null;
        double? tiffFocalLength = null;
        double? tiffExposureBias = null;
        DateTime? tiffDate = null;
        bool? tiffFlash = null;
        int tiffImageWidth = 0;
        int tiffImageHeight = 0;
        int? tiff35mmFocal = null;
        double? tiffMaxAperture = null;
        string? tiffMeteringMode = null;
        string? tiffExposureProgram = null;
        string? tiffExposureMode = null;
        string? tiffWhiteBalance = null;
        string? tiffSoftware = null;
        string? tiffSerialNumber = null;
        string? tiffUniqueModel = null;

        // Fila de IFDs para processar (incluindo SubIFDs)
        var ifdOffsets = new Queue<uint>();
        var visitedIfds = new HashSet<uint>();
        if (firstIfdOffset > 0 && firstIfdOffset < fs.Length)
        {
            ifdOffsets.Enqueue(firstIfdOffset);
        }

        Span<byte> nextIfdBuf = stackalloc byte[4];
        Span<byte> countBuf = stackalloc byte[2];
        Span<byte> entry = stackalloc byte[12];
        Span<byte> subIfdBuf = stackalloc byte[4];

        while (ifdOffsets.Count > 0 && visitedIfds.Count < 20)
        {
            uint currentIfdOffset = ifdOffsets.Dequeue();
            if (!visitedIfds.Add(currentIfdOffset)) continue;
            if (currentIfdOffset >= fs.Length - 2) continue;

            fs.Seek(currentIfdOffset, SeekOrigin.Begin);
            if (fs.Read(countBuf) < 2) break;

            ushort numEntries = isLittleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(countBuf)
                : BinaryPrimitives.ReadUInt16BigEndian(countBuf);

            if (numEntries > 500) continue; // Evitar dados corrompidos

            long currentJpegOffset = 0;
            long currentJpegLength = 0;
            long currentStripOffset = 0;
            long currentStripCount = 0;

            for (int i = 0; i < numEntries; i++)
            {
                if (fs.Read(entry) < 12) break;

                ushort tag = isLittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(entry[0..2])
                    : BinaryPrimitives.ReadUInt16BigEndian(entry[0..2]);
                ushort type = isLittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(entry[2..4])
                    : BinaryPrimitives.ReadUInt16BigEndian(entry[2..4]);
                uint count = isLittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(entry[4..8])
                    : BinaryPrimitives.ReadUInt32BigEndian(entry[4..8]);
                uint valOffset = isLittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(entry[8..12])
                    : BinaryPrimitives.ReadUInt32BigEndian(entry[8..12]);

                switch (tag)
                {
                    case 0x0112: // Orientation
                        if (type is 3 or 4) // SHORT or LONG
                        {
                            orientation = (int)(isLittleEndian ? (ushort)valOffset : (valOffset >> 16));
                            if (orientation < 1 || orientation > 8) orientation = 1;
                        }
                        break;

                    case 0x010F: // Make (Fabricante)
                        if (string.IsNullOrEmpty(tiffMake))
                        {
                            string? m = ReadTiffString(fs, valOffset, count, entry[8..12], isLittleEndian);
                            if (!string.IsNullOrWhiteSpace(m) && !m.Any(c => char.IsControl(c) || c > 127))
                            {
                                tiffMake = m.Trim();
                            }
                        }
                        break;

                    case 0xC614: // UniqueCameraModel (DNG)
                        if (string.IsNullOrEmpty(tiffUniqueModel))
                        {
                            tiffUniqueModel = ReadTiffString(fs, valOffset, count, entry[8..12], isLittleEndian);
                        }
                        break;

                    case 0x0131: // Software
                        if (string.IsNullOrEmpty(tiffSoftware))
                        {
                            tiffSoftware = ReadTiffString(fs, valOffset, count, entry[8..12], isLittleEndian);
                        }
                        break;

                    case 0xC62F: // CameraSerialNumber / ProfileName
                    case 0xA431: // BodySerialNumber
                        if (string.IsNullOrEmpty(tiffSerialNumber))
                        {
                            tiffSerialNumber = ReadTiffString(fs, valOffset, count, entry[8..12], isLittleEndian);
                        }
                        break;

                    case 0x9205: // MaxApertureValue
                        if (!tiffMaxAperture.HasValue)
                        {
                            tiffMaxAperture = ReadTiffRational(fs, valOffset, isLittleEndian);
                        }
                        break;

                    case 0x9207: // MeteringMode
                        if (tiffMeteringMode == null)
                        {
                            ushort mm = (ushort)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                            tiffMeteringMode = mm switch
                            {
                                1 => "Média",
                                2 => "Ponderada Central",
                                3 => "Pontual",
                                4 => "Multi-ponto",
                                5 => "Matricial (Multi-segmento)",
                                6 => "Parcial",
                                _ => "Padrão"
                            };
                        }
                        break;

                    case 0x8822: // ExposureProgram
                        if (tiffExposureProgram == null)
                        {
                            ushort ep = (ushort)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                            tiffExposureProgram = ep switch
                            {
                                1 => "Manual (M)",
                                2 => "Programa Normal (P)",
                                3 => "Prioridade de Abertura (Av)",
                                4 => "Prioridade de Obturador (Tv)",
                                5 => "Modo Criativo",
                                6 => "Ação / Esporte",
                                7 => "Retrato",
                                8 => "Paisagem",
                                _ => "Automático"
                            };
                        }
                        break;

                    case 0xA402: // ExposureMode
                        if (tiffExposureMode == null)
                        {
                            ushort em = (ushort)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                            tiffExposureMode = em switch
                            {
                                1 => "Manual",
                                2 => "Auto Bracketing",
                                _ => "Automático"
                            };
                        }
                        break;

                    case 0xA403: // WhiteBalance
                        if (tiffWhiteBalance == null)
                        {
                            ushort wb = (ushort)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                            tiffWhiteBalance = wb switch
                            {
                                1 => "Manual",
                                _ => "Automático"
                            };
                        }
                        break;

                    case 0xA405: // FocalLengthIn35mmFilm
                        if (!tiff35mmFocal.HasValue)
                        {
                            tiff35mmFocal = (int)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                        }
                        break;

                    case 0x0110: // Model (Modelo)
                        if (string.IsNullOrEmpty(tiffModel))
                        {
                            tiffModel = ReadTiffString(fs, valOffset, count, entry[8..12], isLittleEndian);
                        }
                        break;

                    case 0x0132: // DateTime
                    case 0x9003: // DateTimeOriginal
                        if (!tiffDate.HasValue)
                        {
                            string? dStr = ReadTiffString(fs, valOffset, count, entry[8..12], isLittleEndian);
                            if (!string.IsNullOrEmpty(dStr) && DateTime.TryParseExact(dStr.Trim(), "yyyy:MM:dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt))
                            {
                                tiffDate = dt;
                            }
                        }
                        break;

                    case 0x9209: // Flash
                        if (!tiffFlash.HasValue)
                        {
                            ushort fVal = (ushort)(isLittleEndian ? valOffset : (valOffset >> 16));
                            tiffFlash = (fVal & 0x0001) != 0;
                        }
                        break;

                    case 0x0100: // ImageWidth
                    case 0xA002: // ExifImageWidth
                        if (tiffImageWidth == 0)
                        {
                            tiffImageWidth = (int)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                        }
                        break;

                    case 0x0101: // ImageLength / Height
                    case 0xA003: // ExifImageHeight
                        if (tiffImageHeight == 0)
                        {
                            tiffImageHeight = (int)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                        }
                        break;

                    case 0x8827: // PhotographicSensitivity / ISOSpeedRatings
                    case 0x8833: // RecommendedExposureIndex
                        if (!tiffIso.HasValue)
                        {
                            tiffIso = (int)(type == 3 ? (isLittleEndian ? (ushort)valOffset : (valOffset >> 16)) : valOffset);
                        }
                        break;

                    case 0x829D: // FNumber
                        if (!tiffFNumber.HasValue)
                        {
                            tiffFNumber = ReadTiffRational(fs, valOffset, isLittleEndian);
                        }
                        break;

                    case 0x829A: // ExposureTime
                        if (!tiffExposure.HasValue)
                        {
                            tiffExposure = ReadTiffRational(fs, valOffset, isLittleEndian);
                        }
                        break;

                    case 0x920A: // FocalLength
                        if (!tiffFocalLength.HasValue)
                        {
                            tiffFocalLength = ReadTiffRational(fs, valOffset, isLittleEndian);
                        }
                        break;

                    case 0x9204: // ExposureBiasValue
                        if (!tiffExposureBias.HasValue)
                        {
                            tiffExposureBias = ReadTiffSRational(fs, valOffset, isLittleEndian);
                        }
                        break;

                    case 0xA434: // LensModel
                        if (string.IsNullOrEmpty(tiffLens))
                        {
                            string? lm = ReadTiffString(fs, valOffset, count, entry[8..12], isLittleEndian);
                            if (!string.IsNullOrWhiteSpace(lm) && lm.Trim().Length > 3)
                            {
                                tiffLens = lm.Trim();
                            }
                        }
                        break;

                    case 0x8769: // ExifIFDPointer
                        if (valOffset > 0 && valOffset < fs.Length)
                        {
                            ifdOffsets.Enqueue(valOffset);
                        }
                        break;

                    case 0x0201: // JPEGInterchangeFormat (Offset)
                        currentJpegOffset = valOffset;
                        break;

                    case 0x0202: // JPEGInterchangeFormatLength (Length)
                        currentJpegLength = valOffset;
                        break;

                    case 0x0111: // StripOffsets
                        if (count == 1) currentStripOffset = valOffset;
                        break;

                    case 0x0117: // StripByteCounts
                        if (count == 1) currentStripCount = valOffset;
                        break;

                    case 0x014A: // SubIFDs
                        if (count == 1)
                        {
                            ifdOffsets.Enqueue(valOffset);
                        }
                        else if (count > 1 && count < 20)
                        {
                            // Ler múltiplos subIFDs
                            long savedPos = fs.Position;
                            fs.Seek(valOffset, SeekOrigin.Begin);
                            for (int s = 0; s < count; s++)
                            {
                                if (fs.Read(subIfdBuf) == 4)
                                {
                                    uint subOffset = isLittleEndian
                                        ? BinaryPrimitives.ReadUInt32LittleEndian(subIfdBuf)
                                        : BinaryPrimitives.ReadUInt32BigEndian(subIfdBuf);
                                    ifdOffsets.Enqueue(subOffset);
                                }
                            }
                            fs.Seek(savedPos, SeekOrigin.Begin);
                        }
                        break;
                }
            }

            // Avalia se encontramos JPEG neste IFD
            if (currentJpegOffset > 0 && currentJpegLength > 1024)
            {
                candidates.Add((currentJpegOffset, currentJpegLength));
            }
            else if (currentStripOffset > 0 && currentStripCount > 1024)
            {
                candidates.Add((currentStripOffset, currentStripCount));
            }

            // Ler offset para o próximo IFD padrão
            if (fs.Read(nextIfdBuf) == 4)
            {
                uint nextIfd = isLittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(nextIfdBuf)
                    : BinaryPrimitives.ReadUInt32BigEndian(nextIfdBuf);
                if (nextIfd > 0 && nextIfd < fs.Length)
                {
                    ifdOffsets.Enqueue(nextIfd);
                }
            }
        }

        // Selecionar o maior preview JPEG encontrado
        (long bestOffset, long bestLength) = (0, 0);
        Span<byte> soi = stackalloc byte[2];
        foreach (var (candOffset, candLength) in candidates)
        {
            if (candOffset + candLength <= fs.Length)
            {
                // Verificar assinatura SOI (0xFF, 0xD8)
                fs.Seek(candOffset, SeekOrigin.Begin);
                if (fs.Read(soi) == 2 && soi[0] == 0xFF && soi[1] == 0xD8)
                {
                    if (candLength > bestLength)
                    {
                        bestOffset = candOffset;
                        bestLength = candLength;
                    }
                }
            }
        }

        if (bestLength > 0)
        {
            fs.Seek(bestOffset, SeekOrigin.Begin);
            byte[] jpegBytes = new byte[bestLength];
            int read = fs.Read(jpegBytes, 0, (int)bestLength);
            if (read == bestLength)
            {
                var (width, height, parsedOrient, model, make, flashFired, dateTaken, pLens, pIso, pFNum, pExp, pFocal, pBias) = ParseJpegInfo(jpegBytes);
                int finalOrient = parsedOrient != 1 ? parsedOrient : orientation;
                int finalWidth = width > 0 ? width : tiffImageWidth;
                int finalHeight = height > 0 ? height : tiffImageHeight;

                string? finalMake = !string.IsNullOrEmpty(tiffMake) ? tiffMake : make;
                string? finalModel = !string.IsNullOrEmpty(tiffModel) ? tiffModel : model;

                if (string.IsNullOrEmpty(finalMake) || finalMake.Contains("DJI", StringComparison.OrdinalIgnoreCase) || (tiffUniqueModel != null && tiffUniqueModel.Contains("DJI", StringComparison.OrdinalIgnoreCase)))
                {
                    if (tiffUniqueModel != null && tiffUniqueModel.Contains("DJI", StringComparison.OrdinalIgnoreCase))
                    {
                        finalMake = "DJI";
                        if (string.IsNullOrEmpty(finalModel) || finalModel.StartsWith("FC", StringComparison.OrdinalIgnoreCase))
                        {
                            finalModel = tiffUniqueModel;
                        }
                    }
                    else if (finalModel != null && finalModel.StartsWith("FC", StringComparison.OrdinalIgnoreCase))
                    {
                        finalMake = "DJI";
                    }
                }

                string? normalizedCamera = NormalizeCameraModel(finalMake, finalModel);
                bool? finalFlash = tiffFlash.HasValue ? tiffFlash : flashFired;
                DateTime? finalDate = tiffDate.HasValue ? tiffDate : dateTaken;

                string? finalLens = (!string.IsNullOrEmpty(tiffLens) && tiffLens.Trim().Length > 3) ? tiffLens.Trim() : pLens;
                int? finalIso = tiffIso.HasValue ? tiffIso : pIso;
                double? finalFNumber = tiffFNumber.HasValue ? tiffFNumber : pFNum;
                double? finalExposure = tiffExposure.HasValue ? tiffExposure : pExp;
                double? finalFocal = tiffFocalLength.HasValue ? tiffFocalLength : pFocal;
                double? finalBias = tiffExposureBias.HasValue ? tiffExposureBias : pBias;

                if (string.IsNullOrEmpty(finalLens) || finalLens.Trim().Length <= 3)
                {
                    if (finalMake == "DJI" || (normalizedCamera != null && normalizedCamera.Contains("DJI")))
                    {
                        string focalStr = finalFocal.HasValue ? $"{finalFocal.Value:0.#}mm" : "9mm";
                        string fStr = finalFNumber.HasValue ? $"f/{finalFNumber.Value:0.#}" : "f/1.8";
                        string eqStr = tiff35mmFocal.HasValue ? $" ({tiff35mmFocal.Value}mm eq)" : "";
                        finalLens = $"DJI {focalStr} {fStr}{eqStr}";
                        if (string.IsNullOrEmpty(finalMake)) finalMake = "DJI";
                    }
                }

                sw.Stop();
                return RawPreviewResult.Ok(
                    jpegBytes, finalOrient, finalWidth, finalHeight, sw.Elapsed.TotalMilliseconds,
                    normalizedCamera, finalMake, finalFlash, finalDate,
                    finalLens, finalIso, finalFNumber, finalExposure, finalFocal, finalBias,
                    tiff35mmFocal, tiffMaxAperture, tiffMeteringMode, tiffExposureProgram, tiffExposureMode, tiffWhiteBalance, tiffSoftware, tiffSerialNumber);
            }
        }

        return RawPreviewResult.Fail("Nenhum preview JPEG válido localizado nos IFDs TIFF.");
    }

    #endregion

    #region Canon CR3 (ISOBMFF)

    private static RawPreviewResult ExtractFromCr3(string filePath, Stopwatch sw)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        long length = fs.Length;

        // Vasculha caixas ISOBMFF recursivamente procurando 'PRVW' ou 'thmb'
        var result = FindCr3PreviewBox(fs, 0, length);
        if (result != null)
        {
            var (width, height, orient, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias) = ParseJpegInfo(result);
            sw.Stop();
            string? normalized = NormalizeCameraModel(make, model);
            return RawPreviewResult.Ok(result, orient, width, height, sw.Elapsed.TotalMilliseconds, normalized, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);
        }

        return RawPreviewResult.Fail("Preview PRVW não encontrado no arquivo CR3.");
    }

    private static byte[]? FindCr3PreviewBox(FileStream fs, long startOffset, long maxOffset)
    {
        fs.Seek(startOffset, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[8];
        Span<byte> prvwHead = stackalloc byte[64];

        while (fs.Position < maxOffset - 8)
        {
            long boxStart = fs.Position;
            if (fs.Read(header) < 8) break;

            uint boxSize = BinaryPrimitives.ReadUInt32BigEndian(header[0..4]);
            string boxType = Encoding.ASCII.GetString(header[4..8]);

            long payloadSize = boxSize == 1
                ? ReadExtendedSize(fs) - 16
                : boxSize - 8;

            long payloadStart = fs.Position;

            if (boxSize == 0 || payloadStart + payloadSize > maxOffset)
            {
                payloadSize = maxOffset - payloadStart;
            }

            // Canon CR3 armazena o preview JPEG no átomo PRVW
            if (boxType == "PRVW")
            {
                // PRVW contém cabeçalho de 14 ou 24 bytes, seguido pelo JPEG
                // Vamos inspecionar os primeiros 32 bytes procurando 0xFF, 0xD8
                fs.Seek(payloadStart, SeekOrigin.Begin);
                int bytesToRead = Math.Min(64, (int)payloadSize);
                int hRead = fs.Read(prvwHead[..bytesToRead]);
                for (int i = 0; i < hRead - 1; i++)
                {
                    if (prvwHead[i] == 0xFF && prvwHead[i + 1] == 0xD8)
                    {
                        long jpegStart = payloadStart + i;
                        long jpegLen = payloadSize - i;
                        fs.Seek(jpegStart, SeekOrigin.Begin);
                        byte[] jpeg = new byte[jpegLen];
                        fs.ReadExactly(jpeg);
                        return jpeg;
                    }
                }
            }

            // Caixas contêineres: moov, uuid
            if (boxType is "moov" or "uuid")
            {
                var inner = FindCr3PreviewBox(fs, payloadStart, payloadStart + payloadSize);
                if (inner != null) return inner;
            }

            long nextPos = payloadStart + payloadSize;
            if (nextPos <= boxStart) break;
            fs.Seek(nextPos, SeekOrigin.Begin);
        }

        return null;
    }

    private static long ReadExtendedSize(FileStream fs)
    {
        Span<byte> ext = stackalloc byte[8];
        fs.ReadExactly(ext);
        return BinaryPrimitives.ReadInt64BigEndian(ext);
    }

    #endregion

    #region Fujifilm RAF

    private static RawPreviewResult ExtractFromRaf(string filePath, Stopwatch sw)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        if (fs.Length < 120) return RawPreviewResult.Fail("Arquivo RAF muito pequeno.");

        Span<byte> magic = stackalloc byte[16];
        fs.ReadExactly(magic);
        string magicStr = Encoding.ASCII.GetString(magic);
        if (!magicStr.StartsWith("FUJIFILMCCD-RAW"))
        {
            return RawPreviewResult.Fail("Assinatura Fujifilm RAF inválida.");
        }

        // Offset 84: Offset do JPEG embutido (Big Endian)
        // Offset 88: Tamanho do JPEG embutido
        fs.Seek(84, SeekOrigin.Begin);
        Span<byte> ptrs = stackalloc byte[8];
        fs.ReadExactly(ptrs);

        uint jpegOffset = BinaryPrimitives.ReadUInt32BigEndian(ptrs[0..4]);
        uint jpegLength = BinaryPrimitives.ReadUInt32BigEndian(ptrs[4..8]);

        if (jpegOffset > 0 && jpegLength > 1024 && jpegOffset + jpegLength <= fs.Length)
        {
            fs.Seek(jpegOffset, SeekOrigin.Begin);
            byte[] jpeg = new byte[jpegLength];
            fs.ReadExactly(jpeg);

            var (width, height, orient, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias) = ParseJpegInfo(jpeg);
            sw.Stop();
            string? normalized = NormalizeCameraModel(make, model);
            return RawPreviewResult.Ok(jpeg, orient, width, height, sw.Elapsed.TotalMilliseconds, normalized, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);
        }

        return RawPreviewResult.Fail("Ponteiro de preview não encontrado no cabeçalho RAF.");
    }

    #endregion

    #region Fallback: Fast Scan por JPEG

    private static RawPreviewResult ScanForEmbeddedJpeg(string filePath, Stopwatch sw)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        int bufferSize = (int)Math.Min(fs.Length, 8 * 1024 * 1024); // Primeiros 8 MB
        byte[] buffer = new byte[bufferSize];
        int bytesRead = fs.Read(buffer, 0, bufferSize);

        // Procura por assinatura SOI (0xFF, 0xD8, 0xFF)
        long bestOffset = -1;
        long bestLength = -1;

        for (int i = 0; i < bytesRead - 4; i++)
        {
            if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8 && buffer[i + 2] == 0xFF)
            {
                // Verifica se parece um JPEG de imagem (APP1 EXIF ou APP0 JFIF ou DQT)
                byte marker = buffer[i + 3];
                if (marker is 0xE1 or 0xE0 or 0xDB)
                {
                    // Encontrar o EOI correspondente (0xFF, 0xD9)
                    int eoi = FindJpegEoi(buffer, i);
                    if (eoi > i + 1024)
                    {
                        long len = eoi - i + 2;
                        if (len > bestLength)
                        {
                            bestOffset = i;
                            bestLength = len;
                        }
                    }
                }
            }
        }

        if (bestOffset >= 0 && bestLength > 5000)
        {
            byte[] jpeg = new byte[bestLength];
            Array.Copy(buffer, bestOffset, jpeg, 0, bestLength);
            var (width, height, orient, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias) = ParseJpegInfo(jpeg);
            sw.Stop();
            string? normalized = NormalizeCameraModel(make, model);
            return RawPreviewResult.Ok(jpeg, orient, width, height, sw.Elapsed.TotalMilliseconds, normalized, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);
        }

        return RawPreviewResult.Fail("Nenhum stream JPEG identificado na varredura.");
    }

    private static int FindJpegEoi(byte[] buffer, int start)
    {
        for (int i = start + 100; i < buffer.Length - 1; i++)
        {
            if (buffer[i] == 0xFF && buffer[i + 1] == 0xD9)
            {
                return i;
            }
        }
        return -1;
    }

    #endregion

    #region JPEG Parser de Dimensão e Orientação

    public static (int width, int height, int orientation, string? model, string? make, bool? flashFired, DateTime? dateTaken, string? lens, int? iso, double? fNum, double? exp, double? focal, double? bias) ParseJpegInfo(ReadOnlySpan<byte> jpeg)
    {
        int width = 0;
        int height = 0;
        int orientation = 1;
        string? model = null;
        string? make = null;
        bool? flashFired = null;
        DateTime? dateTaken = null;
        string? lens = null;
        int? iso = null;
        double? fNum = null;
        double? exp = null;
        double? focal = null;
        double? bias = null;

        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return (width, height, orientation, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);
        }

        int pos = 2;
        while (pos < jpeg.Length - 8)
        {
            if (jpeg[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            byte marker = jpeg[pos + 1];
            pos += 2;

            if (marker is 0xD9 or 0xDA) // EOI ou SOS (Start of Scan)
            {
                break;
            }

            if (marker is 0xD8 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (pos + 2 > jpeg.Length) break;
            ushort segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(pos, 2));

            // Marcador APP1 (EXIF) -> Orientação e Metadados
            if (marker == 0xE1 && segmentLength > 14)
            {
                var app1 = jpeg.Slice(pos + 2, segmentLength - 2);
                if (app1.Length > 6 && Encoding.ASCII.GetString(app1[..4]) == "Exif" && app1[4] == 0 && app1[5] == 0)
                {
                    var exif = ParseExifData(app1[6..]);
                    if (exif.orientation >= 1 && exif.orientation <= 8) orientation = exif.orientation;
                    if (!string.IsNullOrEmpty(exif.model)) model = exif.model;
                    if (!string.IsNullOrEmpty(exif.make)) make = exif.make;
                    if (exif.flashFired.HasValue) flashFired = exif.flashFired;
                    if (exif.dateTaken.HasValue) dateTaken = exif.dateTaken;
                    if (!string.IsNullOrEmpty(exif.lens)) lens = exif.lens;
                    if (exif.iso.HasValue) iso = exif.iso;
                    if (exif.fNum.HasValue) fNum = exif.fNum;
                    if (exif.exp.HasValue) exp = exif.exp;
                    if (exif.focal.HasValue) focal = exif.focal;
                    if (exif.bias.HasValue) bias = exif.bias;
                }
            }

            // Marcadores SOF0, SOF1, SOF2 -> Dimensões
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (pos + 7 < jpeg.Length)
                {
                    height = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(pos + 3, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(pos + 5, 2));
                }
            }

            pos += segmentLength;
        }

        return (width, height, orientation, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);
    }

    private static (int orientation, string? model, string? make, bool? flashFired, DateTime? dateTaken, string? lens, int? iso, double? fNum, double? exp, double? focal, double? bias) ParseExifData(ReadOnlySpan<byte> tiffData)
    {
        int orientation = 1;
        string? model = null;
        string? make = null;
        bool? flashFired = null;
        DateTime? dateTaken = null;
        string? lens = null;
        int? iso = null;
        double? fNum = null;
        double? exp = null;
        double? focal = null;
        double? bias = null;

        if (tiffData.Length < 8) return (orientation, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);

        bool isLE;
        if (tiffData[0] == 0x49 && tiffData[1] == 0x49) isLE = true;
        else if (tiffData[0] == 0x4D && tiffData[1] == 0x4D) isLE = false;
        else return (orientation, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);

        uint ifd0Offset = isLE
            ? BinaryPrimitives.ReadUInt32LittleEndian(tiffData.Slice(4, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(tiffData.Slice(4, 4));

        if (ifd0Offset + 2 > tiffData.Length) return (orientation, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);

        uint exifSubIfdOffset = 0;

        ushort entries = isLE
            ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice((int)ifd0Offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice((int)ifd0Offset, 2));

        int entryPos = (int)ifd0Offset + 2;
        for (int i = 0; i < entries && entryPos + 12 <= tiffData.Length; i++, entryPos += 12)
        {
            ushort tag = isLE
                ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice(entryPos, 2))
                : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice(entryPos, 2));
            uint count = isLE
                ? BinaryPrimitives.ReadUInt32LittleEndian(tiffData.Slice(entryPos + 4, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(tiffData.Slice(entryPos + 4, 4));
            uint valOffset = isLE
                ? BinaryPrimitives.ReadUInt32LittleEndian(tiffData.Slice(entryPos + 8, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(tiffData.Slice(entryPos + 8, 4));

            switch (tag)
            {
                case 0x0112: // Orientation
                    orientation = isLE
                        ? (int)BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice(entryPos + 8, 2))
                        : (int)BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice(entryPos + 8, 2));
                    break;
                case 0x0110: // Model
                    model = ReadAsciiString(tiffData, valOffset, count);
                    break;
                case 0x010F: // Make
                    make = ReadAsciiString(tiffData, valOffset, count);
                    break;
                case 0x8769: // Exif SubIFD
                    exifSubIfdOffset = valOffset;
                    break;
            }
        }

        // Se houver Exif SubIFD, ler Flash (0x9209) e DateTimeOriginal (0x9003)
        if (exifSubIfdOffset > 0 && exifSubIfdOffset + 2 <= tiffData.Length)
        {
            ushort subEntries = isLE
                ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice((int)exifSubIfdOffset, 2))
                : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice((int)exifSubIfdOffset, 2));

            int subPos = (int)exifSubIfdOffset + 2;
            for (int i = 0; i < subEntries && subPos + 12 <= tiffData.Length; i++, subPos += 12)
            {
                ushort tag = isLE
                    ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice(subPos, 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice(subPos, 2));
                uint count = isLE
                    ? BinaryPrimitives.ReadUInt32LittleEndian(tiffData.Slice(subPos + 4, 4))
                    : BinaryPrimitives.ReadUInt32BigEndian(tiffData.Slice(subPos + 4, 4));
                uint valOffset = isLE
                    ? BinaryPrimitives.ReadUInt32LittleEndian(tiffData.Slice(subPos + 8, 4))
                    : BinaryPrimitives.ReadUInt32BigEndian(tiffData.Slice(subPos + 8, 4));

                if (tag == 0x9209) // Flash
                {
                    ushort flashVal = isLE
                        ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice(subPos + 8, 2))
                        : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice(subPos + 8, 2));
                    flashFired = (flashVal & 0x0001) != 0;
                }
                else if (tag == 0x9003 || tag == 0x0132 || tag == 0x9004) // DateTimeOriginal ou DateTime
                {
                    string? dtStr = ReadAsciiString(tiffData, valOffset, count);
                    if (!string.IsNullOrEmpty(dtStr) && DateTime.TryParseExact(dtStr.Trim(), "yyyy:MM:dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        dateTaken = dt;
                    }
                }
                else if (tag == 0x8827 || tag == 0x8833) // ISO
                {
                    iso = isLE
                        ? (int)BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice(subPos + 8, 2))
                        : (int)BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice(subPos + 8, 2));
                }
                else if (tag == 0x829D) // FNumber
                {
                    fNum = ReadRational(tiffData, valOffset, isLE);
                }
                else if (tag == 0x829A) // ExposureTime
                {
                    exp = ReadRational(tiffData, valOffset, isLE);
                }
                else if (tag == 0x920A) // FocalLength
                {
                    focal = ReadRational(tiffData, valOffset, isLE);
                }
                else if (tag == 0x9204) // ExposureBiasValue
                {
                    bias = ReadSRational(tiffData, valOffset, isLE);
                }
                else if (tag == 0xA434) // LensModel
                {
                    lens = ReadAsciiString(tiffData, valOffset, count);
                }
            }
        }

        return (orientation, model, make, flashFired, dateTaken, lens, iso, fNum, exp, focal, bias);
    }

    private static double? ReadRational(ReadOnlySpan<byte> data, uint offset, bool isLE)
    {
        if (offset + 8 > data.Length) return null;
        uint num = isLE ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)offset, 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.Slice((int)offset, 4));
        uint den = isLE ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((int)offset + 4, 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.Slice((int)offset + 4, 4));
        if (den == 0) return null;
        return (double)num / den;
    }

    private static double? ReadSRational(ReadOnlySpan<byte> data, uint offset, bool isLE)
    {
        if (offset + 8 > data.Length) return null;
        int num = isLE ? BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)offset, 4)) : BinaryPrimitives.ReadInt32BigEndian(data.Slice((int)offset, 4));
        int den = isLE ? BinaryPrimitives.ReadInt32LittleEndian(data.Slice((int)offset + 4, 4)) : BinaryPrimitives.ReadInt32BigEndian(data.Slice((int)offset + 4, 4));
        if (den == 0) return null;
        return (double)num / den;
    }

    private static string? ReadAsciiString(ReadOnlySpan<byte> data, uint offset, uint count)
    {
        if (count == 0 || offset >= data.Length) return null;
        int len = (int)Math.Min(count, (uint)(data.Length - offset));
        var strSpan = data.Slice((int)offset, len);
        int nullIdx = strSpan.IndexOf((byte)0);
        if (nullIdx >= 0) strSpan = strSpan[..nullIdx];
        string result = Encoding.ASCII.GetString(strSpan).Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    #endregion
}
