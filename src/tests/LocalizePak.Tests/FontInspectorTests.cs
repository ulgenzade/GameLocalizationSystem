using System;
using System.IO;
using System.Text;
using LocalizePak.IO.Services;
using Xunit;

namespace LocalizePak.Tests;

public class FontInspectorTests
{
    private readonly FontInspectorService _service = new();

    [Fact]
    public void InspectFont_SmallOrNullData_ReturnsInvalidFont()
    {
        var report = _service.InspectFont(Array.Empty<byte>(), "test.ttf");
        Assert.False(report.IsValidFont);
        Assert.Contains("küçük veya boş", report.StatusSummary);
    }

    [Fact]
    public void InspectFont_BmFontText_DetectsTurkishGlyphs()
    {
        // ğ = 287 (0x011F), Ğ = 286 (0x011E), ı = 305 (0x0131), İ = 304 (0x0130)
        // ö = 246, Ö = 214, ü = 252, Ü = 220, ş = 351 (0x015F), Ş = 350 (0x015E)
        // ç = 231, Ç = 199
        var sb = new StringBuilder();
        sb.AppendLine("info face=\"Arial\" size=32 bold=0 italic=0");
        sb.AppendLine("common lineHeight=32 base=26 scaleW=512 scaleH=512");
        sb.AppendLine("chars count=12");
        sb.AppendLine("char id=287 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=286 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=305 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=304 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=246 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=214 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=252 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=220 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=351 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=350 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=231 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");
        sb.AppendLine("char id=199 x=0 y=0 width=10 height=10 xoffset=0 yoffset=0 xadvance=10");

        byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
        var report = _service.InspectFont(data, "font.fnt");

        Assert.True(report.IsValidFont);
        Assert.True(report.HasAllTurkishGlyphs);
        Assert.Equal(12, report.TotalSupportedTurkishGlyphs);
        Assert.Empty(report.MissingGlyphs);
        Assert.Contains("Tüm Türkçe karakterler destekleniyor", report.StatusSummary);
    }

    [Fact]
    public void InspectFont_BmFontText_MissingSomeGlyphs_ReportsMissing()
    {
        // Yalnızca ç ve Ç var, diğerleri eksik
        var sb = new StringBuilder();
        sb.AppendLine("chars count=2");
        sb.AppendLine("char id=231 x=0 y=0"); // ç
        sb.AppendLine("char id=199 x=0 y=0"); // Ç

        byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
        var report = _service.InspectFont(data, "font.fnt");

        Assert.True(report.IsValidFont);
        Assert.False(report.HasAllTurkishGlyphs);
        Assert.Equal(2, report.TotalSupportedTurkishGlyphs);
        Assert.Equal(10, report.MissingGlyphs.Count);
        Assert.Contains('ğ', report.MissingGlyphs);
        Assert.Contains('ı', report.MissingGlyphs);
        Assert.Contains('ş', report.MissingGlyphs);
    }

    [Fact]
    public void InspectFont_SystemArialFont_IfPresent_ValidatesCorrectly()
    {
        // Windows sistem fontu Arial.ttf kontrolü
        string arialPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (File.Exists(arialPath))
        {
            byte[] fontData = File.ReadAllBytes(arialPath);
            var report = _service.InspectFont(fontData, "arial.ttf");

            Assert.True(report.IsValidFont);
            Assert.Contains("TrueType", report.FontFormat);
            Assert.True(report.HasAllTurkishGlyphs);
            Assert.Empty(report.MissingGlyphs);
            Assert.Equal("Arial", report.FontFamily);
        }
    }
}
