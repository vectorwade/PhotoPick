using System;
using System.Collections.Generic;

namespace PhotoPick.Core.Models;

public class XmpMetadata
{
    public int Rating { get; set; }
    public string? Label { get; set; }
    public DateTime? CreateDate { get; set; }
    public string? Creator { get; set; }
    public string? Title { get; set; }
    public List<string> SubjectKeywords { get; set; } = [];
    public Dictionary<string, string> RawAttributes { get; set; } = [];
}
