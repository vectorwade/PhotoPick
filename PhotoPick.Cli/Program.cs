using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoPick.Core.Services;

namespace PhotoPick.Cli;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=============================================================");
        Console.WriteLine(" PhotoPick CLI — Motor de Extração de RAW & Integração XMP  ");
        Console.WriteLine("=============================================================");
        Console.WriteLine();

        string folder = args.Length > 0 ? args[0] : "";
        if (args.Contains("--benchmark") || args.Contains("-b"))
        {
            await RunSyntheticBenchmarkAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Console.WriteLine("Uso: PhotoPick.Cli <caminho_da_pasta_de_fotos> ou PhotoPick.Cli --benchmark");
            Console.WriteLine();
            Console.WriteLine("Executando benchmark sintético automaticamente...");
            await RunSyntheticBenchmarkAsync();
            return;
        }

        await ProcessFolderAsync(folder);
    }

    private static async Task ProcessFolderAsync(string folder)
    {
        var extractor = new RawPreviewExtractor();
        var xmpService = new XmpService();

        var files = Directory.EnumerateFiles(folder)
            .Where(RawPreviewExtractor.IsSupported)
            .ToList();

        Console.WriteLine($"Pasta selecionada: {folder}");
        Console.WriteLine($"Encontrados {files.Count} arquivos suportados para triagem.");
        Console.WriteLine();

        if (files.Count == 0)
        {
            Console.WriteLine("Nenhum arquivo RAW/JPG suportado encontrado na pasta.");
            return;
        }

        Console.WriteLine("Iniciando benchmark de extração de previews embutidos...");
        var totalSw = Stopwatch.StartNew();
        int successCount = 0;
        double totalExtractionMs = 0;

        foreach (var file in files.Take(20))
        {
            var res = extractor.ExtractPreview(file);
            if (res.Success)
            {
                successCount++;
                totalExtractionMs += res.ElapsedMilliseconds;
                Console.WriteLine($"  [OK] {Path.GetFileName(file)} | {res.Width}x{res.Height} | {res.JpegBytes?.Length / 1024} KB | {res.ElapsedMilliseconds:F2} ms");
            }
            else
            {
                Console.WriteLine($"  [FALHA] {Path.GetFileName(file)}: {res.ErrorMessage} ({res.ElapsedMilliseconds:F2} ms)");
            }
        }

        totalSw.Stop();
        Console.WriteLine();
        Console.WriteLine($"Benchmark concluído:");
        Console.WriteLine($"  Fotos processadas: {Math.Min(20, files.Count)}");
        Console.WriteLine($"  Sucessos: {successCount}");
        Console.WriteLine($"  Tempo médio por preview: {(successCount > 0 ? totalExtractionMs / successCount : 0):F2} ms");
        Console.WriteLine($"  Tempo total: {totalSw.ElapsedMilliseconds} ms");
    }

    private static async Task RunSyntheticBenchmarkAsync()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "PhotoPick_Bench_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Console.WriteLine($"Criando 50 arquivos RAW sintéticos com previews embutidos em {tempDir}...");
            byte[] mockJpeg = CreateMockJpeg(1920, 1080);

            for (int i = 1; i <= 50; i++)
            {
                string path = Path.Combine(tempDir, $"IMG_{i:D4}.CR2");
                CreateMockTiffWithEmbeddedJpeg(path, mockJpeg, orientation: 1);
            }

            Console.WriteLine("Arquivos criados. Executando medição de alta precisão...");
            await ProcessFolderAsync(tempDir);

            // Teste de XMP
            Console.WriteLine();
            Console.WriteLine("Testando gravação de XMP em lote para o Lightroom Classic...");
            var xmpService = new XmpService();
            var swXmp = Stopwatch.StartNew();
            for (int i = 1; i <= 50; i++)
            {
                string path = Path.Combine(tempDir, $"IMG_{i:D4}.CR2");
                int rating = (i % 5) + 1;
                string? label = (i % 2 == 0) ? "Green" : "Red";
                xmpService.WriteMetadata(path, rating, label);
            }
            swXmp.Stop();
            Console.WriteLine($"Gravados 50 arquivos .xmp em {swXmp.ElapsedMilliseconds} ms ({(double)swXmp.ElapsedMilliseconds / 50:F2} ms por arquivo)!");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static byte[] CreateMockJpeg(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]); // SOI
        ms.Write([0xFF, 0xE0, 0x00, 0x10]);
        ms.Write("JFIF\0"u8);
        ms.Write([0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);

        ms.Write([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        Span<byte> dim = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(dim[0..2], (ushort)height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(dim[2..4], (ushort)width);
        ms.Write(dim);
        ms.Write([0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);

        byte[] dummy = new byte[8192];
        Array.Fill<byte>(dummy, 0xBB);
        ms.Write(dummy);
        ms.Write([0xFF, 0xD9]); // EOI
        return ms.ToArray();
    }

    private static void CreateMockTiffWithEmbeddedJpeg(string filePath, byte[] jpegData, ushort orientation)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write((byte)0x49);
        bw.Write((byte)0x49);
        bw.Write((ushort)42);
        bw.Write((uint)8);

        ushort numEntries = 3;
        bw.Write(numEntries);

        uint jpegOffset = (uint)(8 + 2 + (numEntries * 12) + 4);
        uint jpegLength = (uint)jpegData.Length;

        bw.Write((ushort)0x0112);
        bw.Write((ushort)3);
        bw.Write((uint)1);
        bw.Write((ushort)orientation);
        bw.Write((ushort)0);

        bw.Write((ushort)0x0201);
        bw.Write((ushort)4);
        bw.Write((uint)1);
        bw.Write(jpegOffset);

        bw.Write((ushort)0x0202);
        bw.Write((ushort)4);
        bw.Write((uint)1);
        bw.Write(jpegLength);

        bw.Write((uint)0);
        bw.Write(jpegData);
    }
}
