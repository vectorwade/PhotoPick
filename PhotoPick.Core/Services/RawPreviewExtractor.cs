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

    #region JPEG Direto

    private static RawPreviewResult ExtractFromJpegFile(string filePath, Stopwatch sw)
    {
        var bytes = File.ReadAllBytes(filePath);
        var (width, height, orientation) = ParseJpegInfo(bytes);
        sw.Stop();
        return RawPreviewResult.Ok(bytes, orientation, width, height, sw.Elapsed.TotalMilliseconds);
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
                var (width, height, parsedOrient) = ParseJpegInfo(jpegBytes);
                int finalOrient = parsedOrient != 1 ? parsedOrient : orientation;
                sw.Stop();
                return RawPreviewResult.Ok(jpegBytes, finalOrient, width, height, sw.Elapsed.TotalMilliseconds);
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
            var (width, height, orient) = ParseJpegInfo(result);
            sw.Stop();
            return RawPreviewResult.Ok(result, orient, width, height, sw.Elapsed.TotalMilliseconds);
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

            var (width, height, orient) = ParseJpegInfo(jpeg);
            sw.Stop();
            return RawPreviewResult.Ok(jpeg, orient, width, height, sw.Elapsed.TotalMilliseconds);
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
            var (width, height, orient) = ParseJpegInfo(jpeg);
            sw.Stop();
            return RawPreviewResult.Ok(jpeg, orient, width, height, sw.Elapsed.TotalMilliseconds);
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

    public static (int width, int height, int orientation) ParseJpegInfo(ReadOnlySpan<byte> jpeg)
    {
        int width = 0;
        int height = 0;
        int orientation = 1;

        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return (width, height, orientation);
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

            // Marcador APP1 (EXIF) -> Orientação
            if (marker == 0xE1 && segmentLength > 14)
            {
                var app1 = jpeg.Slice(pos + 2, segmentLength - 2);
                if (app1.Length > 6 && Encoding.ASCII.GetString(app1[..4]) == "Exif" && app1[4] == 0 && app1[5] == 0)
                {
                    int parsedOrient = ParseExifOrientation(app1[6..]);
                    if (parsedOrient >= 1 && parsedOrient <= 8)
                    {
                        orientation = parsedOrient;
                    }
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

        return (width, height, orientation);
    }

    private static int ParseExifOrientation(ReadOnlySpan<byte> tiffData)
    {
        if (tiffData.Length < 8) return 1;

        bool isLE;
        if (tiffData[0] == 0x49 && tiffData[1] == 0x49) isLE = true;
        else if (tiffData[0] == 0x4D && tiffData[1] == 0x4D) isLE = false;
        else return 1;

        uint ifd0Offset = isLE
            ? BinaryPrimitives.ReadUInt32LittleEndian(tiffData.Slice(4, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(tiffData.Slice(4, 4));

        if (ifd0Offset + 2 > tiffData.Length) return 1;

        ushort entries = isLE
            ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice((int)ifd0Offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice((int)ifd0Offset, 2));

        int entryPos = (int)ifd0Offset + 2;
        for (int i = 0; i < entries && entryPos + 12 <= tiffData.Length; i++, entryPos += 12)
        {
            ushort tag = isLE
                ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice(entryPos, 2))
                : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice(entryPos, 2));

            if (tag == 0x0112) // Orientation
            {
                return isLE
                    ? BinaryPrimitives.ReadUInt16LittleEndian(tiffData.Slice(entryPos + 8, 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(tiffData.Slice(entryPos + 8, 2));
            }
        }

        return 1;
    }

    #endregion
}
