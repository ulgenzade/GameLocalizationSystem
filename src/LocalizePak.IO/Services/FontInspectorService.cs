using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Services;

/// <summary>
/// Oyun arşivlerindeki font varlıklarını (.ttf, .otf, .fnt, TextMeshPro vb.) denetleyen,
/// Türkçe alfabe glif desteğini (ç, Ç, ğ, Ğ, ı, İ, ö, Ö, ş, Ş, ü, Ü) analiz eden servis.
/// </summary>
public class FontInspectorService
{
    public static readonly IReadOnlyList<char> StandardTurkishChars = new[]
    {
        'ç', 'Ç', 'ğ', 'Ğ', 'ı', 'İ', 'ö', 'Ö', 'ş', 'Ş', 'ü', 'Ü'
    };

    public static readonly IReadOnlyList<char> ExtendedTurkishChars = new[]
    {
        'ç', 'Ç', 'ğ', 'Ğ', 'ı', 'İ', 'ö', 'Ö', 'ş', 'Ş', 'ü', 'Ü',
        'â', 'Â', 'î', 'Î', 'û', 'Û'
    };

    /// <summary>
    /// Verilen bayt dizisini font olarak analiz eder ve Türkçe glif raporunu üretir.
    /// </summary>
    public FontReport InspectFont(byte[] data, string fileName, bool includeExtendedTurkish = false)
    {
        var targetChars = includeExtendedTurkish ? ExtendedTurkishChars : StandardTurkishChars;

        if (data == null || data.Length < 12)
        {
            return new FontReport
            {
                FileName = fileName,
                IsValidFont = false,
                FontFormat = "Geçersiz",
                StatusSummary = "Dosya boyutu font analizi için çok küçük veya boş."
            };
        }

        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        // 1. BMFont / TextMeshPro metin veya XML tabanlı font kontrolü
        if (ext == ".fnt" || ext == ".xml" || ext == ".json" || ext == ".txt")
        {
            return InspectTextBasedFont(data, fileName, targetChars);
        }

        // 2. TrueType / OpenType ikili font kontrolü
        return InspectTrueTypeFont(data, fileName, targetChars);
    }

    private FontReport InspectTextBasedFont(byte[] data, string fileName, IReadOnlyList<char> targetChars)
    {
        string text = Encoding.UTF8.GetString(data);
        var charSet = new HashSet<int>();

        // BMFont format: char id=305 or id="305" or "id": 305
        var matches = Regex.Matches(text, @"(?:char\s+id=|id=[""']?|""id""\s*:\s*)(\d+)", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Groups[1].Value, out int id))
            {
                charSet.Add(id);
            }
        }

        var supported = new List<char>();
        var missing = new List<char>();

        foreach (var ch in targetChars)
        {
            if (charSet.Contains(ch))
            {
                supported.Add(ch);
            }
            else
            {
                missing.Add(ch);
            }
        }

        bool hasAll = missing.Count == 0;
        string summary = hasAll
            ? "✅ Tüm Türkçe karakterler destekleniyor."
            : $"⚠️ {missing.Count} eksik Türkçe karakter: {string.Join(", ", missing)}";

        return new FontReport
        {
            FileName = fileName,
            FontFormat = "BMFont / TextMeshPro SDF",
            IsValidFont = charSet.Count > 0,
            HasAllTurkishGlyphs = hasAll,
            TotalSupportedTurkishGlyphs = supported.Count,
            TotalRequiredTurkishGlyphs = targetChars.Count,
            SupportedGlyphs = supported,
            MissingGlyphs = missing,
            StatusSummary = summary
        };
    }

    private FontReport InspectTrueTypeFont(byte[] data, string fileName, IReadOnlyList<char> targetChars)
    {
        var span = data.AsSpan();
        int fontOffset = 0;

        // TTC (TrueType Collection) başlığı kontrolü: 'ttcf'
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(0, 4));
        if (magic == 0x74746366) // 'ttcf'
        {
            if (span.Length >= 16)
            {
                uint numFonts = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4));
                if (numFonts > 0)
                {
                    fontOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(12, 4));
                    if (fontOffset + 12 > span.Length) fontOffset = 0;
                }
            }
        }

        if (fontOffset + 12 > span.Length)
        {
            return new FontReport
            {
                FileName = fileName,
                IsValidFont = false,
                FontFormat = "Bilinmeyen İkili",
                StatusSummary = "Font başlığı okunamadı."
            };
        }

        var fontSpan = span.Slice(fontOffset);
        uint sfntVersion = BinaryPrimitives.ReadUInt32BigEndian(fontSpan.Slice(0, 4));
        string fontFormat = sfntVersion switch
        {
            0x00010000 => "TrueType (TTF)",
            0x4F54544F => "OpenType CFF (OTF)",
            0x74727565 => "TrueType (Mac OS)",
            0x74797031 => "PostScript Type 1",
            _ => "Tanımlanamayan SFNT"
        };

        if (fontFormat == "Tanımlanamayan SFNT")
        {
            return new FontReport
            {
                FileName = fileName,
                IsValidFont = false,
                FontFormat = fontFormat,
                StatusSummary = "Geçersiz veya desteklenmeyen font imzası."
            };
        }

        ushort numTables = BinaryPrimitives.ReadUInt16BigEndian(fontSpan.Slice(4, 2));
        int tableDirOffset = 12;

        int cmapOffset = -1;
        int cmapLength = -1;
        int nameOffset = -1;
        int nameLength = -1;

        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = tableDirOffset + (i * 16);
            if (entryOffset + 16 > fontSpan.Length) break;

            uint tag = BinaryPrimitives.ReadUInt32BigEndian(fontSpan.Slice(entryOffset, 4));
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(fontSpan.Slice(entryOffset + 8, 4));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(fontSpan.Slice(entryOffset + 12, 4));

            // 'cmap' = 0x636D6170
            if (tag == 0x636D6170)
            {
                cmapOffset = (int)offset;
                cmapLength = (int)length;
            }
            // 'name' = 0x6E616D65
            else if (tag == 0x6E616D65)
            {
                nameOffset = (int)offset;
                nameLength = (int)length;
            }
        }

        if (cmapOffset < 0 || cmapOffset >= span.Length)
        {
            return new FontReport
            {
                FileName = fileName,
                IsValidFont = true,
                FontFormat = fontFormat,
                StatusSummary = "Font içinde 'cmap' (karakter-glif eşleme) tablosu bulunamadı."
            };
        }

        // Font adını oku
        string? fontFamily = null;
        string? fullFontName = null;
        if (nameOffset >= 0 && nameOffset + nameLength <= span.Length)
        {
            (fontFamily, fullFontName) = ParseNameTable(span.Slice(nameOffset, nameLength));
        }

        // Glifleri tara
        var supported = new List<char>();
        var missing = new List<char>();

        var cmapSpan = span.Slice(cmapOffset);
        foreach (char c in targetChars)
        {
            if (HasGlyphForChar(cmapSpan, c))
            {
                supported.Add(c);
            }
            else
            {
                missing.Add(c);
            }
        }

        bool hasAll = missing.Count == 0;
        string summary = hasAll
            ? "✅ Tüm Türkçe karakterler destekleniyor."
            : $"⚠️ {missing.Count} eksik Türkçe karakter: {string.Join(", ", missing)}";

        return new FontReport
        {
            FileName = fileName,
            FontFamily = fontFamily,
            FullFontName = fullFontName,
            FontFormat = fontFormat,
            IsValidFont = true,
            HasAllTurkishGlyphs = hasAll,
            TotalSupportedTurkishGlyphs = supported.Count,
            TotalRequiredTurkishGlyphs = targetChars.Count,
            SupportedGlyphs = supported,
            MissingGlyphs = missing,
            StatusSummary = summary
        };
    }

    private bool HasGlyphForChar(ReadOnlySpan<byte> cmapSpan, char targetChar)
    {
        if (cmapSpan.Length < 4) return false;

        ushort numTables = BinaryPrimitives.ReadUInt16BigEndian(cmapSpan.Slice(2, 2));
        int selectedSubtableOffset = -1;
        int preferredRank = -1;

        for (int i = 0; i < numTables; i++)
        {
            int recOffset = 4 + (i * 8);
            if (recOffset + 8 > cmapSpan.Length) break;

            ushort platformId = BinaryPrimitives.ReadUInt16BigEndian(cmapSpan.Slice(recOffset, 2));
            ushort encodingId = BinaryPrimitives.ReadUInt16BigEndian(cmapSpan.Slice(recOffset + 2, 2));
            uint subOffset = BinaryPrimitives.ReadUInt32BigEndian(cmapSpan.Slice(recOffset + 4, 4));

            int rank = 0;
            // Windows Unicode BMP (Platform 3, Encoding 1)
            if (platformId == 3 && encodingId == 1) rank = 100;
            // Windows Unicode Full (Platform 3, Encoding 10)
            else if (platformId == 3 && encodingId == 10) rank = 110;
            // Unicode Consortium (Platform 0)
            else if (platformId == 0) rank = 90;
            // Macintosh Roman (Platform 1, Encoding 0)
            else if (platformId == 1 && encodingId == 0) rank = 10;

            if (rank > preferredRank && subOffset < cmapSpan.Length)
            {
                preferredRank = rank;
                selectedSubtableOffset = (int)subOffset;
            }
        }

        if (selectedSubtableOffset < 0 || selectedSubtableOffset + 6 > cmapSpan.Length)
        {
            return false;
        }

        var subSpan = cmapSpan.Slice(selectedSubtableOffset);
        ushort format = BinaryPrimitives.ReadUInt16BigEndian(subSpan.Slice(0, 2));

        if (format == 4)
        {
            return CheckFormat4Glyph(subSpan, targetChar);
        }
        else if (format == 12)
        {
            return CheckFormat12Glyph(subSpan, targetChar);
        }
        else if (format == 0 && targetChar <= 255)
        {
            return CheckFormat0Glyph(subSpan, (byte)targetChar);
        }

        return false;
    }

    private bool CheckFormat4Glyph(ReadOnlySpan<byte> subSpan, char c)
    {
        if (subSpan.Length < 14) return false;

        ushort length = BinaryPrimitives.ReadUInt16BigEndian(subSpan.Slice(2, 2));
        if (length > subSpan.Length) length = (ushort)subSpan.Length;

        ushort segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(subSpan.Slice(6, 2));
        int segCount = segCountX2 / 2;

        int endCodeOffset = 14;
        int startCodeOffset = endCodeOffset + (segCount * 2) + 2; // +2 reservedPad
        int idDeltaOffset = startCodeOffset + (segCount * 2);
        int idRangeOffsetBase = idDeltaOffset + (segCount * 2);

        if (idRangeOffsetBase + (segCount * 2) > length) return false;

        int charCode = c;

        for (int i = 0; i < segCount; i++)
        {
            ushort endCode = BinaryPrimitives.ReadUInt16BigEndian(subSpan.Slice(endCodeOffset + (i * 2), 2));
            if (endCode >= charCode)
            {
                ushort startCode = BinaryPrimitives.ReadUInt16BigEndian(subSpan.Slice(startCodeOffset + (i * 2), 2));
                if (charCode < startCode)
                {
                    return false; // Bulunamadı
                }

                short idDelta = BinaryPrimitives.ReadInt16BigEndian(subSpan.Slice(idDeltaOffset + (i * 2), 2));
                int idRangeOffsetLocation = idRangeOffsetBase + (i * 2);
                ushort idRangeOffset = BinaryPrimitives.ReadUInt16BigEndian(subSpan.Slice(idRangeOffsetLocation, 2));

                if (idRangeOffset == 0)
                {
                    int glyphId = (charCode + idDelta) & 0xFFFF;
                    return glyphId != 0;
                }
                else
                {
                    int glyphOffset = idRangeOffsetLocation + idRangeOffset + ((charCode - startCode) * 2);
                    if (glyphOffset + 2 <= length)
                    {
                        ushort glyphId = BinaryPrimitives.ReadUInt16BigEndian(subSpan.Slice(glyphOffset, 2));
                        if (glyphId != 0)
                        {
                            glyphId = (ushort)((glyphId + idDelta) & 0xFFFF);
                            return glyphId != 0;
                        }
                    }
                    return false;
                }
            }
        }

        return false;
    }

    private bool CheckFormat12Glyph(ReadOnlySpan<byte> subSpan, char c)
    {
        if (subSpan.Length < 16) return false;

        uint numGroups = BinaryPrimitives.ReadUInt32BigEndian(subSpan.Slice(12, 4));
        int groupsOffset = 16;
        uint charCode = c;

        for (int i = 0; i < numGroups; i++)
        {
            int entryOffset = groupsOffset + (i * 12);
            if (entryOffset + 12 > subSpan.Length) break;

            uint startCharCode = BinaryPrimitives.ReadUInt32BigEndian(subSpan.Slice(entryOffset, 4));
            uint endCharCode = BinaryPrimitives.ReadUInt32BigEndian(subSpan.Slice(entryOffset + 4, 4));
            uint startGlyphID = BinaryPrimitives.ReadUInt32BigEndian(subSpan.Slice(entryOffset + 8, 4));

            if (charCode >= startCharCode && charCode <= endCharCode)
            {
                uint glyphId = startGlyphID + (charCode - startCharCode);
                return glyphId > 0;
            }
        }

        return false;
    }

    private bool CheckFormat0Glyph(ReadOnlySpan<byte> subSpan, byte c)
    {
        if (subSpan.Length < 6 + 256) return false;
        byte glyphId = subSpan[6 + c];
        return glyphId != 0;
    }

    private (string? Family, string? FullName) ParseNameTable(ReadOnlySpan<byte> nameSpan)
    {
        if (nameSpan.Length < 6) return (null, null);

        ushort count = BinaryPrimitives.ReadUInt16BigEndian(nameSpan.Slice(2, 2));
        ushort stringOffset = BinaryPrimitives.ReadUInt16BigEndian(nameSpan.Slice(4, 2));

        string? family = null;
        string? fullName = null;

        for (int i = 0; i < count; i++)
        {
            int recOffset = 6 + (i * 12);
            if (recOffset + 12 > nameSpan.Length) break;

            ushort platformId = BinaryPrimitives.ReadUInt16BigEndian(nameSpan.Slice(recOffset, 2));
            ushort nameId = BinaryPrimitives.ReadUInt16BigEndian(nameSpan.Slice(recOffset + 6, 2));
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(nameSpan.Slice(recOffset + 8, 2));
            ushort offset = BinaryPrimitives.ReadUInt16BigEndian(nameSpan.Slice(recOffset + 10, 2));

            if (nameId != 1 && nameId != 4) continue;

            int strPos = stringOffset + offset;
            if (strPos + length <= nameSpan.Length)
            {
                var strSpan = nameSpan.Slice(strPos, length);
                string strVal;

                if (platformId == 0 || platformId == 3)
                {
                    strVal = Encoding.BigEndianUnicode.GetString(strSpan);
                }
                else
                {
                    strVal = Encoding.UTF8.GetString(strSpan);
                }

                if (nameId == 1 && family == null) family = strVal;
                if (nameId == 4 && fullName == null) fullName = strVal;
            }
        }

        return (family, fullName);
    }
}
