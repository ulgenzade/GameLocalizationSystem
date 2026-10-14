using System.Text;
using System.Text.RegularExpressions;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// GNU Gettext (.po / .pot) yerelleştirme dosyaları için endüstriyel ayrıştırıcı ve derleyici.
/// Unreal Engine GatherText, Godot Engine ve açık kaynak oyun projeleriyle tam uyumludur.
/// </summary>
public static class PoCodec
{
    /// <summary>
    /// Verilen metnin bir Gettext PO / POT dosyası olup olmadığını tespit eder.
    /// </summary>
    public static bool IsPoContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return (content.Contains("msgid ") || content.Contains("msgid\t") || content.Contains("msgid\"")) &&
               (content.Contains("msgstr ") || content.Contains("msgstr\t") || content.Contains("msgstr\"") || content.Contains("msgstr["));
    }

    /// <summary>
    /// .po / .pot içeriğini LocresFile modeline dönüştürür.
    /// </summary>
    public static LocresFile Parse(string content, string? sourcePath = null)
    {
        var locres = new LocresFile
        {
            Version = LocresVersion.V3,
            SourcePath = sourcePath ?? "Localization.po"
        };

        var defaultNs = new LocresNamespace { Name = "Gettext" };
        var namespacesByName = new Dictionary<string, LocresNamespace>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gettext"] = defaultNs
        };

        using var reader = new StringReader(content);
        string? line;

        string currentContext = string.Empty;
        var currentComments = new List<string>();
        string currentMsgId = string.Empty;
        string currentMsgStr = string.Empty;

        // Okuma durumu: 0 = Yok, 1 = msgctxt, 2 = msgid, 3 = msgstr
        int readState = 0;
        var currentBuffer = new StringBuilder();

        void CommitEntry()
        {
            if (readState == 3)
            {
                currentMsgStr = currentBuffer.ToString();
            }

            // msgid boş ise PO başlık (header) bloğudur; normal çeviri satırı olarak ekleme
            if (!string.IsNullOrEmpty(currentMsgId))
            {
                var nsName = !string.IsNullOrWhiteSpace(currentContext) ? currentContext : "Gettext";
                if (!namespacesByName.TryGetValue(nsName, out var ns))
                {
                    ns = new LocresNamespace { Name = nsName };
                    namespacesByName[nsName] = ns;
                }

                var key = !string.IsNullOrWhiteSpace(currentContext)
                    ? $"{currentContext}_{currentMsgId.GetHashCode():X8}"
                    : $"PO_{ns.Entries.Count + 1:D5}";

                var entry = new LocalizationEntry
                {
                    NamespaceName = nsName,
                    Key = key,
                    SourceText = currentMsgId,
                    TranslatedText = currentMsgStr,
                    Status = string.IsNullOrWhiteSpace(currentMsgStr) ? TranslationStatus.Pending : TranslationStatus.Done,
                    Notes = currentComments.Count > 0 ? string.Join("\n", currentComments) : null
                };

                ns.Entries.Add(entry);
            }

            currentContext = string.Empty;
            currentComments.Clear();
            currentMsgId = string.Empty;
            currentMsgStr = string.Empty;
            currentBuffer.Clear();
            readState = 0;
        }

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();

            // Yorum satırları
            if (trimmed.StartsWith('#'))
            {
                if (readState == 3 && currentBuffer.Length > 0)
                {
                    CommitEntry();
                }
                currentComments.Add(line);
                continue;
            }

            // Boş satır -> Girdi sonu
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (readState > 0)
                {
                    CommitEntry();
                }
                continue;
            }

            // msgctxt "bağlam"
            if (trimmed.StartsWith("msgctxt", StringComparison.Ordinal))
            {
                if (readState == 3) CommitEntry();
                readState = 1;
                currentBuffer.Clear();
                var text = ExtractQuotedString(trimmed.Substring("msgctxt".Length).Trim());
                currentBuffer.Append(text);
                continue;
            }

            // msgid "kaynak"
            if (trimmed.StartsWith("msgid", StringComparison.Ordinal) && !trimmed.StartsWith("msgid_plural", StringComparison.Ordinal))
            {
                if (readState == 1)
                {
                    currentContext = currentBuffer.ToString();
                    currentBuffer.Clear();
                }
                else if (readState == 3)
                {
                    CommitEntry();
                }

                readState = 2;
                currentBuffer.Clear();
                var text = ExtractQuotedString(trimmed.Substring("msgid".Length).Trim());
                currentBuffer.Append(text);
                continue;
            }

            // msgstr "hedef" veya msgstr[0] "hedef"
            if (trimmed.StartsWith("msgstr", StringComparison.Ordinal))
            {
                if (readState == 2)
                {
                    currentMsgId = currentBuffer.ToString();
                    currentBuffer.Clear();
                }

                readState = 3;
                var afterMsgStr = trimmed.Substring("msgstr".Length).Trim();
                if (afterMsgStr.StartsWith('['))
                {
                    var closeBracket = afterMsgStr.IndexOf(']');
                    if (closeBracket >= 0)
                        afterMsgStr = afterMsgStr.Substring(closeBracket + 1).Trim();
                }

                var text = ExtractQuotedString(afterMsgStr);
                currentBuffer.Append(text);
                continue;
            }

            // Çok satırlı dizgi devamı: "metin"
            if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && readState > 0)
            {
                var text = ExtractQuotedString(trimmed);
                currentBuffer.Append(text);
            }
        }

        // Dosya sonundaki son girdiyi kaydet
        if (readState > 0)
        {
            CommitEntry();
        }

        foreach (var kvp in namespacesByName)
        {
            if (kvp.Value.Entries.Count > 0)
            {
                locres.Namespaces.Add(kvp.Value);
            }
        }

        return locres;
    }

    /// <summary>
    /// LocresFile modelini eksiksiz, RFC uyumlu Gettext .po formatında metin olarak serileştirir.
    /// </summary>
    public static string Serialize(LocresFile locresFile)
    {
        var sb = new StringBuilder();

        // Standart PO Başlığı
        sb.AppendLine("# Translation file generated by GLS (Game Localization System)");
        sb.AppendLine("# https://github.com/ulgenzade/GameLocalizationSystem");
        sb.AppendLine("msgid \"\"");
        sb.AppendLine("msgstr \"\"");
        sb.AppendLine("\"Project-Id-Version: GLS Localized Game\\n\"");
        sb.AppendLine("\"MIME-Version: 1.0\\n\"");
        sb.AppendLine("\"Content-Type: text/plain; charset=UTF-8\\n\"");
        sb.AppendLine("\"Content-Transfer-Encoding: 8bit\\n\"");
        sb.AppendLine($"\"PO-Revision-Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm+0000}\\n\"");
        sb.AppendLine();

        foreach (var ns in locresFile.Namespaces)
        {
            foreach (var entry in ns.Entries)
            {
                // Yorumlar varsa ekle
                if (!string.IsNullOrWhiteSpace(entry.Notes))
                {
                    var lines = entry.Notes.Split('\n');
                    foreach (var c in lines)
                    {
                        var t = c.Trim();
                        if (!string.IsNullOrEmpty(t))
                            sb.AppendLine(t.StartsWith('#') ? t : $"# {t}");
                    }
                }

                // Namespace Gettext veya boş değilse msgctxt olarak yaz
                if (!string.IsNullOrWhiteSpace(entry.NamespaceName) &&
                    !entry.NamespaceName.Equals("Gettext", StringComparison.OrdinalIgnoreCase) &&
                    !entry.NamespaceName.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"msgctxt \"{EscapeString(entry.NamespaceName)}\"");
                }

                // msgid
                FormatMultilineString(sb, "msgid", entry.SourceText ?? string.Empty);

                // msgstr (çeviri boşsa boş bırak)
                var target = !string.IsNullOrEmpty(entry.TranslatedText) ? entry.TranslatedText : string.Empty;
                FormatMultilineString(sb, "msgstr", target);

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static void FormatMultilineString(StringBuilder sb, string prefix, string text)
    {
        if (text.Contains('\n'))
        {
            sb.AppendLine($"{prefix} \"\"");
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var suffix = (i < lines.Length - 1) ? "\\n" : "";
                sb.AppendLine($"\"{EscapeString(line)}{suffix}\"");
            }
        }
        else
        {
            sb.AppendLine($"{prefix} \"{EscapeString(text)}\"");
        }
    }

    private static string ExtractQuotedString(string s)
    {
        var trimmed = s.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }
        return UnescapeString(trimmed);
    }

    private static string EscapeString(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
    }

    private static string UnescapeString(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char next = s[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case '"': sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    default:
                        sb.Append(next);
                        i++;
                        break;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }
}
