using System;
using System.Buffers.Binary;
using System.IO;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class RawPreviewExtractorTests : IDisposable
{
    private readonly string _testDir;
    private readonly RawPreviewExtractor _extractor;

    public RawPreviewExtractorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "RawPreviewTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _extractor = new RawPreviewExtractor();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExtractPreview_SyntheticTiffRaw_ExtractsEmbeddedJpegInstantaneously()
    {
        // 1. Cria um payload JPEG sintético válido
        byte[] mockJpeg = CreateMockJpeg(width: 800, height: 600);

        // 2. Monta arquivo TIFF mock (similar a .CR2 / .ARW / .NEF / .DNG)
        string fakeRawPath = Path.Combine(_testDir, "SAMPLE_RAW.CR2");
        CreateMockTiffWithEmbeddedJpeg(fakeRawPath, mockJpeg, orientation: 6);

        // 3. Executa a extração
        var result = _extractor.ExtractPreview(fakeRawPath);

        // 4. Validações
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.JpegBytes);
        Assert.Equal(mockJpeg.Length, result.JpegBytes.Length);
        Assert.Equal(6, result.Orientation);
        Assert.True(result.ElapsedMilliseconds < 50, $"Extração demorou {result.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ExtractPreview_DirectJpegFile_ExtractsCorrectly()
    {
        byte[] mockJpeg = CreateMockJpeg(width: 1920, height: 1080);
        string jpegPath = Path.Combine(_testDir, "PHOTO.JPG");
        File.WriteAllBytes(jpegPath, mockJpeg);

        var result = _extractor.ExtractPreview(jpegPath);

        Assert.True(result.Success);
        Assert.NotNull(result.JpegBytes);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
    }

    private static byte[] CreateMockJpeg(int width, int height)
    {
        using var ms = new MemoryStream();
        // SOI
        ms.Write([0xFF, 0xD8]);

        // APP0 (JFIF)
        ms.Write([0xFF, 0xE0, 0x00, 0x10]);
        ms.Write("JFIF\0"u8);
        ms.Write([0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);

        // SOF0 (Baseline DCT - dimensions)
        ms.Write([0xFF, 0xC0, 0x00, 0x11, 0x08]); // Length 17, precision 8
        Span<byte> dim = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(dim[0..2], (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(dim[2..4], (ushort)width);
        ms.Write(dim);
        ms.Write([0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]); // 3 components

        // Dummy payload data
        byte[] dummyData = new byte[2048];
        Array.Fill<byte>(dummyData, 0xAA);
        ms.Write(dummyData);

        // EOI
        ms.Write([0xFF, 0xD9]);
        return ms.ToArray();
    }

    private static void CreateMockTiffWithEmbeddedJpeg(string filePath, byte[] jpegData, ushort orientation)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Header TIFF Little Endian ("II")
        bw.Write((byte)0x49);
        bw.Write((byte)0x49);
        bw.Write((ushort)42); // Magic
        bw.Write((uint)8);    // Offset IFD0 = 8

        // IFD0
        ushort numEntries = 3;
        bw.Write(numEntries);

        uint jpegOffset = (uint)(8 + 2 + (numEntries * 12) + 4); // imediatamente após IFD0
        uint jpegLength = (uint)jpegData.Length;

        // Entry 1: Orientation (Tag 0x0112, Type 3 SHORT, Count 1)
        bw.Write((ushort)0x0112);
        bw.Write((ushort)3);
        bw.Write((uint)1);
        bw.Write((ushort)orientation);
        bw.Write((ushort)0); // padding

        // Entry 2: JPEGInterchangeFormat (Tag 0x0201, Type 4 LONG, Count 1)
        bw.Write((ushort)0x0201);
        bw.Write((ushort)4);
        bw.Write((uint)1);
        bw.Write(jpegOffset);

        // Entry 3: JPEGInterchangeFormatLength (Tag 0x0202, Type 4 LONG, Count 1)
        bw.Write((ushort)0x0202);
        bw.Write((ushort)4);
        bw.Write((uint)1);
        bw.Write(jpegLength);

        // Next IFD offset = 0
        bw.Write((uint)0);

        // Write JPEG bytes
        bw.Write(jpegData);
    }

    [Fact]
    public void ExtractPreview_RealDjiDng_IfPresent_ExtractsSuccessfully()
    {
        string path = @"H:\DCIM\DJI_001\DJI_20260805151722_0305_D.DNG";
        if (!File.Exists(path)) return;

        var res = _extractor.ExtractPreview(path);
        Assert.True(res.Success, res.ErrorMessage);
        Assert.NotNull(res.JpegBytes);
        Assert.True(res.JpegBytes.Length > 10000);
        Assert.NotNull(res.CameraModel);
        Assert.Contains("DJI", res.CameraModel);
    }
}
