using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using PhotoPick.Core.Models;

namespace PhotoPick.Core.Services;

public interface IXmpService
{
    string GetXmpPath(string photoPath);
    bool XmpExists(string photoPath);
    XmpMetadata ReadMetadata(string photoPath);
    void WriteMetadata(string photoPath, int rating, string? colorLabel);
    Task WriteMetadataBatchAsync(IEnumerable<PhotoItem> items, CancellationToken ct = default);
}

public class XmpService : IXmpService
{
    private static readonly XNamespace NsRdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace NsXmp = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace NsPhotoshop = "http://ns.adobe.com/photoshop/1.0/";
    private static readonly XNamespace NsDc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace NsX = "adobe:ns:meta/";

    public string GetXmpPath(string photoPath)
    {
        return Path.ChangeExtension(photoPath, ".xmp");
    }

    public bool XmpExists(string photoPath)
    {
        return File.Exists(GetXmpPath(photoPath));
    }

    public XmpMetadata ReadMetadata(string photoPath)
    {
        var meta = new XmpMetadata();
        string xmpPath = GetXmpPath(photoPath);

        if (!File.Exists(xmpPath))
        {
            return meta;
        }

        try
        {
            string content = File.ReadAllText(xmpPath, Encoding.UTF8);
            var doc = XDocument.Parse(content);

            var desc = doc.Descendants(NsRdf + "Description").FirstOrDefault();
            if (desc == null) return meta;

            // 1. Rating
            var ratingElem = desc.Element(NsXmp + "Rating");
            if (ratingElem != null && int.TryParse(ratingElem.Value, out int r))
            {
                meta.Rating = Math.Clamp(r, 0, 5);
            }
            else
            {
                var ratingAttr = desc.Attribute(NsXmp + "Rating");
                if (ratingAttr != null && int.TryParse(ratingAttr.Value, out int ra))
                {
                    meta.Rating = Math.Clamp(ra, 0, 5);
                }
            }

            // 2. Label
            var labelElem = desc.Element(NsXmp + "Label");
            if (labelElem != null)
            {
                meta.Label = labelElem.Value;
            }
            else
            {
                var labelAttr = desc.Attribute(NsXmp + "Label");
                if (labelAttr != null)
                {
                    meta.Label = labelAttr.Value;
                }
            }

            // 3. Keywords
            var subjectBag = desc.Element(NsDc + "subject")?.Element(NsRdf + "Bag");
            if (subjectBag != null)
            {
                foreach (var li in subjectBag.Elements(NsRdf + "li"))
                {
                    if (!string.IsNullOrWhiteSpace(li.Value))
                    {
                        meta.SubjectKeywords.Add(li.Value.Trim());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Em caso de erro ao parsear XMP existente, retorna o que conseguiu
            meta.RawAttributes["ReadError"] = ex.Message;
        }

        return meta;
    }

    public void WriteMetadata(string photoPath, int rating, string? colorLabel)
    {
        string xmpPath = GetXmpPath(photoPath);
        rating = Math.Clamp(rating, 0, 5);

        XDocument doc;
        XElement desc;

        if (File.Exists(xmpPath))
        {
            try
            {
                string content = File.ReadAllText(xmpPath, Encoding.UTF8);
                doc = XDocument.Parse(content);
                desc = doc.Descendants(NsRdf + "Description").FirstOrDefault()
                       ?? CreateMinimalDescription(doc);
            }
            catch
            {
                // Se o XMP existente estiver corrompido, recria documento limpo
                (doc, desc) = CreateNewXmpDocument();
            }
        }
        else
        {
            (doc, desc) = CreateNewXmpDocument();
        }

        // Atualizar Rating
        var ratingElem = desc.Element(NsXmp + "Rating");
        var ratingAttr = desc.Attribute(NsXmp + "Rating");
        ratingAttr?.Remove(); // Remove atributo inline se houver para padronizar elemento

        if (rating > 0)
        {
            if (ratingElem == null)
            {
                desc.Add(new XElement(NsXmp + "Rating", rating));
            }
            else
            {
                ratingElem.Value = rating.ToString();
            }
        }
        else
        {
            ratingElem?.Remove();
        }

        // Atualizar Label
        var labelElem = desc.Element(NsXmp + "Label");
        var labelAttr = desc.Attribute(NsXmp + "Label");
        labelAttr?.Remove();

        if (!string.IsNullOrWhiteSpace(colorLabel))
        {
            if (labelElem == null)
            {
                desc.Add(new XElement(NsXmp + "Label", colorLabel.Trim()));
            }
            else
            {
                labelElem.Value = colorLabel.Trim();
            }
        }
        else
        {
            labelElem?.Remove();
        }

        // Gravação atômica: escreve em arquivo temporário e faz swap
        string tempPath = xmpPath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
            sb.AppendLine(doc.ToString());
            sb.Append("<?xpacket end=\"w\"?>");

            File.WriteAllText(tempPath, sb.ToString(), Encoding.UTF8);
            File.Move(tempPath, xmpPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public Task WriteMetadataBatchAsync(IEnumerable<PhotoItem> items, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                WriteMetadata(item.FilePath, item.Rating, item.ColorLabel);
                item.IsModified = false;
            }
        }, ct);
    }

    private static (XDocument doc, XElement desc) CreateNewXmpDocument()
    {
        var desc = new XElement(NsRdf + "Description",
            new XAttribute(NsRdf + "about", ""),
            new XAttribute(XNamespace.Xmlns + "xmp", NsXmp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "photoshop", NsPhotoshop.NamespaceName)
        );

        var doc = new XDocument(
            new XElement(NsX + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", NsX.NamespaceName),
                new XAttribute(NsX + "xmptk", "Adobe XMP Core 5.6-c140"),
                new XElement(NsRdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", NsRdf.NamespaceName),
                    desc
                )
            )
        );

        return (doc, desc);
    }

    private static XElement CreateMinimalDescription(XDocument doc)
    {
        var rdf = doc.Descendants(NsRdf + "RDF").FirstOrDefault();
        if (rdf == null)
        {
            rdf = new XElement(NsRdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", NsRdf.NamespaceName));
            doc.Root?.Add(rdf);
        }

        var desc = new XElement(NsRdf + "Description",
            new XAttribute(NsRdf + "about", ""),
            new XAttribute(XNamespace.Xmlns + "xmp", NsXmp.NamespaceName)
        );
        rdf.Add(desc);
        return desc;
    }
}
