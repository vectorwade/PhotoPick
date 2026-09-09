using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using PhotoPick.Core.Services;
using Xunit;

namespace PhotoPick.Tests;

public class XmpServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly XmpService _xmpService;

    public XmpServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "PhotoPickTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _xmpService = new XmpService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WriteAndReadMetadata_NewFile_SetsRatingAndLabelCorrectly()
    {
        string photoPath = Path.Combine(_testDir, "DSC_0001.CR3");
        string xmpPath = _xmpService.GetXmpPath(photoPath);

        Assert.False(File.Exists(xmpPath));

        _xmpService.WriteMetadata(photoPath, rating: 5, colorLabel: "Green");

        Assert.True(File.Exists(xmpPath));

        var meta = _xmpService.ReadMetadata(photoPath);
        Assert.Equal(5, meta.Rating);
        Assert.Equal("Green", meta.Label);
    }

    [Fact]
    public void WriteMetadata_PreservesExistingTags_LikeKeywordsAndCopyright()
    {
        string photoPath = Path.Combine(_testDir, "TEST_PHOTO.ARW");
        string xmpPath = _xmpService.GetXmpPath(photoPath);

        // Cria XMP prévio com tags customizadas do fotógrafo
        string initialXmp = """
        <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
         <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
          <rdf:Description rdf:about=""
            xmlns:dc="http://purl.org/dc/elements/1.1/"
            xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/">
           <dc:rights>
            <rdf:Alt><rdf:li xml:lang="x-default">© 2026 Time Fotógrafas</rdf:li></rdf:Alt>
           </dc:rights>
           <dc:subject>
            <rdf:Bag>
             <rdf:li>Ensaio Externo</rdf:li>
             <rdf:li>Golden Hour</rdf:li>
            </rdf:Bag>
           </dc:subject>
          </rdf:Description>
         </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;
        File.WriteAllText(xmpPath, initialXmp, Encoding.UTF8);

        // Atualiza apenas rating e label
        _xmpService.WriteMetadata(photoPath, rating: 4, colorLabel: "Yellow");

        var meta = _xmpService.ReadMetadata(photoPath);
        Assert.Equal(4, meta.Rating);
        Assert.Equal("Yellow", meta.Label);
        Assert.Contains("Ensaio Externo", meta.SubjectKeywords);
        Assert.Contains("Golden Hour", meta.SubjectKeywords);

        // Verifica se a tag de copyright original ainda está no arquivo físico
        string updatedContent = File.ReadAllText(xmpPath);
        Assert.Contains("© 2026 Time Fotógrafas", updatedContent);
    }

    [Fact]
    public void WriteMetadata_ClearRatingAndLabel_RemovesTags()
    {
        string photoPath = Path.Combine(_testDir, "CLEAR_TEST.NEF");

        // Cria com 3 estrelas e Vermelho
        _xmpService.WriteMetadata(photoPath, rating: 3, colorLabel: "Red");
        var meta1 = _xmpService.ReadMetadata(photoPath);
        Assert.Equal(3, meta1.Rating);
        Assert.Equal("Red", meta1.Label);

        // Limpa rating (0) e cor (null)
        _xmpService.WriteMetadata(photoPath, rating: 0, colorLabel: null);
        var meta2 = _xmpService.ReadMetadata(photoPath);
        Assert.Equal(0, meta2.Rating);
        Assert.Null(meta2.Label);
    }
}
