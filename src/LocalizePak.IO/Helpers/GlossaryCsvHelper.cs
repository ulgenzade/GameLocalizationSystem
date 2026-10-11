using System.Text;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Glosary (terimler sözlüğü) girdilerini CSV formatında içe/dışa aktaran yardımcı sınıf.
/// </summary>
public static class GlossaryCsvHelper
{
    /// <summary>
    /// CSV dosyasını okur ve Glosary listesi döndürür.
    /// Format: Orijinal,Ceviri,Cevrilmesin
    /// </summary>
    public static async Task<List<GlossaryEntry>> ImportFromCsvAsync(string filePath)
    {
        var entries = new List<GlossaryEntry>();
        if (!File.Exists(filePath)) return entries;

        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Başlık satırını atla
            if (i == 0 && (line.StartsWith("Original", StringComparison.OrdinalIgnoreCase) ||
                           line.StartsWith("Orijinal", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var parts = ParseCsvLine(line);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;

            var original = parts[0];
            var translation = parts.Length > 1 ? parts[1] : string.Empty;
            var doNotTranslate = parts.Length > 2 && (parts[2].Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                                      parts[2].Equals("1", StringComparison.OrdinalIgnoreCase) ||
                                                      parts[2].Equals("evet", StringComparison.OrdinalIgnoreCase));

            entries.Add(new GlossaryEntry
            {
                Original = original,
                Translation = string.IsNullOrWhiteSpace(translation) ? null : translation,
                DoNotTranslate = doNotTranslate || string.IsNullOrWhiteSpace(translation)
            });
        }

        return entries;
    }

    /// <summary>
    /// Glosary listesini CSV dosyasına kaydeder.
    /// </summary>
    public static async Task ExportToCsvAsync(string filePath, IEnumerable<GlossaryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Original,Translation,DoNotTranslate");
        foreach (var e in entries)
        {
            var orig = Escape(e.Original);
            var trans = Escape(e.Translation ?? string.Empty);
            var dnt = e.DoNotTranslate ? "true" : "false";
            sb.AppendLine($"\"{orig}\",\"{trans}\",{dnt}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static string[] ParseCsvLine(string line)
    {
        var parts = new List<string>();
        bool inQuotes = false;
        var cur = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                parts.Add(cur.ToString().Trim());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }
        parts.Add(cur.ToString().Trim());
        return [.. parts];
    }

    private static string Escape(string text) => text.Replace("\"", "\"\"");
}
