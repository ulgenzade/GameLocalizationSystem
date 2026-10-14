using System;
using System.Collections.Generic;

namespace LocalizePak.Core.Models;

/// <summary>
/// Bir oyun font dosyasının Türkçe karakter ve glif uyumluluk raporu.
/// </summary>
public class FontReport
{
    public string FileName { get; set; } = string.Empty;
    public string? FontFamily { get; set; }
    public string? FullFontName { get; set; }
    public string FontFormat { get; set; } = "Bilinmiyor";
    public bool IsValidFont { get; set; }
    public bool HasAllTurkishGlyphs { get; set; }
    public int TotalSupportedTurkishGlyphs { get; set; }
    public int TotalRequiredTurkishGlyphs { get; set; }
    public IReadOnlyList<char> MissingGlyphs { get; set; } = Array.Empty<char>();
    public IReadOnlyList<char> SupportedGlyphs { get; set; } = Array.Empty<char>();
    public string StatusSummary { get; set; } = string.Empty;

    public double TurkishSupportRatio => TotalRequiredTurkishGlyphs == 0 
        ? 1.0 
        : (double)TotalSupportedTurkishGlyphs / TotalRequiredTurkishGlyphs;
}
