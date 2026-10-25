using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// .locres, XML, LSX, JSON, CSV ve TXT dahil tüm oyun lokalizasyon formatlarını
/// otomatik tespit eden, ayrıştıran ve serileştiren evrensel servis.
/// </summary>
public sealed class UniversalLocalizationService : ILocresSerializer
{
    private readonly LocresService _locresService = new();

    /// <summary>
    /// Çok dilli tablolarda (CSV/TSV) sütun seçimi için kaynak ve hedef dil ISO kodları (ör. ("en", "tr")).
    /// Arayüz, seçili dilleri buradan verir; atanmazsa İngilizce → Türkçe varsayılır.
    /// </summary>
    public Func<(string Source, string Target)>? LanguagePreference { get; set; }

    /// <inheritdoc/>
    public async Task<LocresFile> DeserializeAsync(Stream stream, CancellationToken ct = default)
    {
        // İlk birkaç byte'tan formatı anla
        var buffer = new byte[Math.Min(512, stream.Length)];
        long originalPos = stream.Position;
        await stream.ReadExactlyAsync(buffer, ct);
        stream.Seek(originalPos, SeekOrigin.Begin);

        // 1. Unreal .locres GUID kontrolü (0x0E, 0x14, 0x74, 0x75...)
        if (buffer.Length >= 16 && buffer[0] == 0x0E && buffer[1] == 0x14 && buffer[2] == 0x74 && buffer[3] == 0x75)
        {
            return await _locresService.DeserializeAsync(stream, ct);
        }

        // 2. İkili (Binary) dosya koruması:
        // .locres haricindeki ikili oyun dosyaları (uasset, uexp, doku, ses, mesh vb.) metin olarak ayrıştırılmamalıdır!
        if (IsBinaryBuffer(buffer))
        {
            throw new InvalidOperationException("Seçilen dosya ikili (binary) bir oyun varlığıdır ve metin/lokalizasyon tablosu içermemektedir.");
        }

        // Metin tabanlı formatlar (XML, JSON, CSV, TXT)
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync(ct);
        var trimmed = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        // 2. XML / LSX / Larian formatı (Eğer XML tag'i içeriyorsa veya '<' ile başlıyorsa)
        if (trimmed.StartsWith('<') ||
            content.Contains("<contentList", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<content", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<save>", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<node", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<?xml", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<header", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("contentuid=", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("handle=\"h", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("uid=\"h", StringComparison.OrdinalIgnoreCase))
        {
            return ParseXmlOrLsx(content);
        }

        // 3. JSON formatı
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return ParseJson(content);
        }

        // 4. Gettext PO / POT formatı (Unreal GatherText, Godot vb.)
        if (PoCodec.IsPoContent(content))
        {
            return PoCodec.Parse(content);
        }

        // 5. Tutarlı sütunlu tablo (CSV / TSV): çok dilli tablolar dahil, biçimi korunarak okunur.
        // (Etiket içeren metinlerde de çalışır; eski ayrıştırıcı '<' gördüğünde düz satırlara düşüyordu.)
        bool hasBom = buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF;
        var (sourceLang, targetLang) = LanguagePreference?.Invoke() ?? ("en", "tr");
        if (DelimitedTableCodec.TryParse(content, hasBom, sourceLang, targetLang, out var table))
        {
            return table;
        }

        // 6. CSV / TSV / INI formatı (Yalnızca XML olmayan ve açık ayırıcı içeren metinler)
        if (!content.Contains('<') && !content.Contains('>') &&
            (content.Contains(',') || content.Contains('\t') || content.Contains('=')))
        {
            return ParseDelimitedText(content);
        }

        // 6. Basit satır satır metin (Fallback)
        return ParsePlainLines(content);
    }

    /// <inheritdoc/>
    public async Task<byte[]> SerializeAsync(LocresFile locresFile, CancellationToken ct = default)
    {
        // Tablodan okunduysa özgün biçimiyle (yalnızca hedef sütun değişerek) yaz
        if (locresFile.Table != null)
        {
            return DelimitedTableCodec.Serialize(locresFile.Table);
        }

        // Format türüne göre serileştir
        var path = locresFile.SourcePath?.ToLowerInvariant() ?? string.Empty;

        if (path.EndsWith(".po") || path.EndsWith(".pot"))
        {
            var poText = PoCodec.Serialize(locresFile);
            return Encoding.UTF8.GetBytes(poText);
        }

        if (path.EndsWith(".xml") || path.EndsWith(".lsx"))
        {
            return SerializeToXml(locresFile);
        }

        if (path.EndsWith(".json"))
        {
            return SerializeToJson(locresFile);
        }

        if (path.EndsWith(".csv") || path.EndsWith(".tsv"))
        {
            return SerializeToCsv(locresFile);
        }

        if (path.EndsWith(".txt"))
        {
            return SerializeToTxt(locresFile);
        }

        // Varsayılan: Unreal .locres ikili formatı
        return await _locresService.SerializeAsync(locresFile, ct);
    }

    private static string SanitizeXmlString(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (ch == 0x9 || ch == 0xA || ch == 0xD ||
                (ch >= 0x20 && ch <= 0xD7FF) ||
                (ch >= 0xE000 && ch <= 0xFFFD))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static LocresFile ParseXmlOrLsx(string xmlContent)
    {
        var locres = new LocresFile { Version = LocresVersion.V3 };
        var ns = new LocresNamespace { Name = "Dialogs" };
        locres.Namespaces.Add(ns);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        var cleanXml = SanitizeXmlString(xmlContent).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        // 1. Ultra Hızlı XmlReader Streaming (Larian <contentList> ve <save> için 20-50ms)
        try
        {
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreWhitespace = true,
                IgnoreComments = true
            };

            using var strReader = new StringReader(cleanXml);
            using var reader = XmlReader.Create(strReader, settings);

            string? currentKey = null;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Name.Equals("content", StringComparison.OrdinalIgnoreCase))
                    {
                        currentKey = reader.GetAttribute("contentuid") ?? reader.GetAttribute("handle") ?? reader.GetAttribute("key");
                    }
                    else if (reader.Name.Equals("attribute", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = reader.GetAttribute("id");
                        var val = reader.GetAttribute("value");
                        var handle = reader.GetAttribute("handle");

                        if (!string.IsNullOrWhiteSpace(handle) && !string.IsNullOrWhiteSpace(val))
                        {
                            var text = CleanSolidText(val.Trim());
                            if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(handle))
                            {
                                ns.Entries.Add(new LocalizationEntry
                                {
                                    Key = handle,
                                    SourceText = text,
                                    TranslatedText = text,
                                    NamespaceName = ns.Name
                                });
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(val) &&
                                 (id.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("CustomText", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("Subtitle", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
                                  id.Equals("Description", StringComparison.OrdinalIgnoreCase)))
                        {
                            var text = CleanSolidText(val.Trim());
                            var key = $"{id}_{ns.Entries.Count + 1}";
                            if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(key))
                            {
                                ns.Entries.Add(new LocalizationEntry
                                {
                                    Key = key,
                                    SourceText = text,
                                    TranslatedText = text,
                                    NamespaceName = ns.Name
                                });
                            }
                        }
                    }
                }
                else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA)
                {
                    if (!string.IsNullOrWhiteSpace(currentKey))
                    {
                        var text = CleanSolidText(reader.Value.Trim());
                        if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(currentKey))
                        {
                            ns.Entries.Add(new LocalizationEntry
                            {
                                Key = currentKey,
                                SourceText = text,
                                TranslatedText = text,
                                NamespaceName = ns.Name
                            });
                        }
                        currentKey = null;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    currentKey = null;
                }
            }

            if (ns.Entries.Count > 0)
                return locres;
        }
        catch
        {
            // XmlReader geçersiz XML'de çökerse XDocument veya Regex'e devam et
        }

        // 2. Standart XDocument veya <root> sarmalama ile dene
        try
        {
            XDocument doc;
            try
            {
                doc = XDocument.Parse(cleanXml);
            }
            catch
            {
                doc = XDocument.Parse("<root>" + cleanXml + "</root>");
            }

            // 2a. Larian Dialog formatı: <TagText><value>...</value></TagText>
            // ve Divinity diyalog XML'i: <content contentuid="...">metin</content>
            var dialogValueNodes = doc.Descendants().Where(e =>
                !e.HasElements && (
                e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("TagText", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(e.Value)).ToList();

            if (dialogValueNodes.Count > 0)
            {
                int dialogIdx = 1;
                foreach (var node in dialogValueNodes)
                {
                    var text = CleanSolidText(node.Value.Trim());
                    var key = node.Parent?.Attribute("contentuid")?.Value
                           ?? node.Parent?.Attribute("handle")?.Value
                           ?? node.Parent?.Attribute("key")?.Value
                           ?? node.Parent?.Attribute("id")?.Value
                           ?? $"dialog_{dialogIdx}";
                    dialogIdx++;

                    if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(key))
                    {
                        ns.Entries.Add(new LocalizationEntry
                        {
                            Key = key,
                            SourceText = text,
                            TranslatedText = text,
                            NamespaceName = ns.Name
                        });
                    }
                }

                if (ns.Entries.Count > 0)
                    return locres;
            }

            // 2b. Standart content/string/entry/attribute/handle/key/id tarama
            var contentNodes = doc.Descendants().Where(e =>
                !e.HasElements && (
                e.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase) ||
                (e.Name.LocalName.Equals("attribute", StringComparison.OrdinalIgnoreCase) &&
                    (e.Attribute("handle") != null ||
                     e.Attribute("contentuid") != null ||
                     e.Attribute("id")?.Value.Equals("Text", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("id")?.Value.Equals("CustomText", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("id")?.Value.Equals("Content", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("id")?.Value.Equals("Subtitle", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("id")?.Value.Equals("Title", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("id")?.Value.Equals("Description", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("id")?.Value.Equals("DisplayName", StringComparison.OrdinalIgnoreCase) == true)) ||
                e.Attribute("contentuid") != null ||
                e.Attribute("handle") != null));

            foreach (var node in contentNodes)
            {
                var handle = node.Attribute("handle")?.Value;
                var contentuid = node.Attribute("contentuid")?.Value;
                var keyAttr = node.Attribute("key")?.Value;
                var idAttr = node.Attribute("id")?.Value;

                var key = !string.IsNullOrWhiteSpace(handle) ? handle
                        : !string.IsNullOrWhiteSpace(contentuid) ? contentuid
                        : !string.IsNullOrWhiteSpace(keyAttr) ? keyAttr
                        : !string.IsNullOrWhiteSpace(idAttr) && !idAttr.Equals("Text", StringComparison.OrdinalIgnoreCase) && !idAttr.Equals("CustomText", StringComparison.OrdinalIgnoreCase) ? idAttr
                        : Guid.NewGuid().ToString();

                var text = !string.IsNullOrWhiteSpace(node.Attribute("value")?.Value)
                    ? node.Attribute("value")!.Value
                    : node.Value;

                text = CleanSolidText(text.Trim());
                if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(key))
                {
                    ns.Entries.Add(new LocalizationEntry
                    {
                        Key = key,
                        SourceText = text,
                        TranslatedText = text,
                        NamespaceName = ns.Name
                    });
                }
            }

            if (ns.Entries.Count > 0)
                return locres;
        }
        catch
        {
        }

        // 3. Regex ile Larian & XML diyaloglarını / handle'larını nokta atışı ayıkla
        ExtractWithRegex(cleanXml, ns, seenKeys);
        if (ns.Entries.Count > 0)
            return locres;

        return ParsePlainLines(xmlContent);
    }

    private static void ExtractWithRegex(string content, LocresNamespace ns, HashSet<string> seenKeys)
    {
        // 3a. Larian binary-prefixed contentList format (solid archive'dan gelen ham stream)
        var solidMatches = System.Text.RegularExpressions.Regex.Matches(
            content,
            @"uid=""(?<key>h[0-9a-fA-Fg_-]+)"">(?<text>[^<]{1,}?)(?=<|$)",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match m in solidMatches)
        {
            var key = m.Groups["key"].Value;
            var rawText = m.Groups["text"].Value.Trim();
            rawText = System.Net.WebUtility.HtmlDecode(rawText);
            var text = CleanSolidText(rawText);
            if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(key))
            {
                ns.Entries.Add(new LocalizationEntry
                {
                    Key = key,
                    SourceText = text,
                    TranslatedText = text,
                    NamespaceName = ns.Name
                });
            }
        }

        // 3b. <content contentuid="...">Text</content>
        var contentMatches = System.Text.RegularExpressions.Regex.Matches(
            content,
            @"<content\s+[^>]*contentuid=""(?<key>[^""]+)""[^>]*>(?<text>.*?)</content>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match m in contentMatches)
        {
            var key = m.Groups["key"].Value;
            var rawText = m.Groups["text"].Value.Trim();
            rawText = System.Net.WebUtility.HtmlDecode(rawText);
            var text = CleanSolidText(rawText);
            if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(key))
            {
                ns.Entries.Add(new LocalizationEntry
                {
                    Key = key,
                    SourceText = text,
                    TranslatedText = text,
                    NamespaceName = ns.Name
                });
            }
        }

        // 3c. handle="...h..." value="..." veya value="..." handle="..." (Larian LSX)
        var handleMatches = System.Text.RegularExpressions.Regex.Matches(
            content,
            @"handle=""(?<key>h[0-9a-fA-F_]+)""[^>]*value=""(?<text>[^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match m in handleMatches)
        {
            var key = m.Groups["key"].Value;
            var rawText = m.Groups["text"].Value.Trim();
            rawText = System.Net.WebUtility.HtmlDecode(rawText);
            var text = CleanSolidText(rawText);
            if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(key))
            {
                ns.Entries.Add(new LocalizationEntry
                {
                    Key = key,
                    SourceText = text,
                    TranslatedText = text,
                    NamespaceName = ns.Name
                });
            }
        }

        var handleMatches2 = System.Text.RegularExpressions.Regex.Matches(
            content,
            @"value=""(?<text>[^""]+)""[^>]*handle=""(?<key>h[0-9a-fA-F_]+)""",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match m in handleMatches2)
        {
            var key = m.Groups["key"].Value;
            var rawText = m.Groups["text"].Value.Trim();
            rawText = System.Net.WebUtility.HtmlDecode(rawText);
            var text = CleanSolidText(rawText);
            if (!string.IsNullOrWhiteSpace(text) && !IsXmlNoise(text) && seenKeys.Add(key))
            {
                ns.Entries.Add(new LocalizationEntry
                {
                    Key = key,
                    SourceText = text,
                    TranslatedText = text,
                    NamespaceName = ns.Name
                });
            }
        }
    }

    private static bool IsXmlNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return true;

        // XML bildirimleri ve etiket artıkları
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("<?xml", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("<save>", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("</save>", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<header", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("</header", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<contentList", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("</contentList", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<region", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("</region", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<node", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("</node", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<version", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("</version", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("!\"<") || trimmed.StartsWith("</") || (trimmed.StartsWith("<") && trimmed.EndsWith(">")))
        {
            return true;
        }

        // Tek karakterlik veya sadece noktalama/sayı kalıntıları
        if (trimmed.Length <= 1 && !char.IsLetter(trimmed[0])) return true;

        // Sadece sayı veya boolean veya GUID
        if (long.TryParse(trimmed, out _) || double.TryParse(trimmed, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)) return true;
        if (bool.TryParse(trimmed, out _)) return true;
        if (Guid.TryParse(trimmed, out _)) return true;

        // Larian separator karakterlerini temizleyip harf oranına bak
        var cleaned = CleanSolidText(trimmed);
        if (cleaned.Length == 0) return true;

        if (cleaned.All(c => !char.IsLetter(c))) return true;

        int letterCount = cleaned.Count(char.IsLetter);
        double letterRatio = (double)letterCount / cleaned.Length;
        if (letterRatio < 0.20 && cleaned.Length > 10)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Larian solid archive formatının alan ayıracı olarak kullandığı
    /// ◆ (U+25C6), ■ (U+25A0), � (U+FFFD) gibi karakterleri metinden temizler.
    /// Bu karakterler encoding sorunu değil; Larian'a özel binary format separator'larıdır.
    /// </summary>
    private static string CleanSolidText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            // Larian separator karakterlerini atla
            if (c == '\u25C6' || c == '\u25A0' || c == '\u25C8' ||
                c == '\u2666' || c == '\uFFFD' || c == '\u25CF' ||
                c == '\u25AA' || c == '\u25AB' || c < 0x20)
                continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }


    private static byte[] SerializeToXml(LocresFile locresFile)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("contentList",
                locresFile.AllEntries.Select(e =>
                    new XElement("content",
                        new XAttribute("contentuid", SanitizeXmlString(e.Key)),
                        SanitizeXmlString(string.IsNullOrEmpty(e.TranslatedText) ? e.SourceText : e.TranslatedText)))
            )
        );

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    // ─── JSON Ayrıştırma ─────────────────────────────────────────────────────

    private static LocresFile ParseJson(string jsonContent)
    {
        var locres = new LocresFile { Version = LocresVersion.V3 };
        var ns = new LocresNamespace { Name = "Default" };
        locres.Namespaces.Add(ns);

        try
        {
            var node = JsonNode.Parse(jsonContent);
            if (node is JsonObject obj)
            {
                foreach (var (key, val) in obj)
                {
                    var text = val?.ToString() ?? string.Empty;
                    ns.Entries.Add(new LocalizationEntry
                    {
                        Key = key,
                        SourceText = text,
                        TranslatedText = text,
                        NamespaceName = ns.Name
                    });
                }
            }
            else if (node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    var key = item?["key"]?.ToString() ?? item?["id"]?.ToString() ?? $"entry_{i}";
                    var text = item?["text"]?.ToString() ?? item?["source"]?.ToString() ?? item?.ToString() ?? string.Empty;

                    ns.Entries.Add(new LocalizationEntry
                    {
                        Key = key,
                        SourceText = text,
                        TranslatedText = text,
                        NamespaceName = ns.Name
                    });
                }
            }
        }
        catch
        {
            return ParsePlainLines(jsonContent);
        }

        return locres;
    }

    private static byte[] SerializeToJson(LocresFile locresFile)
    {
        var obj = new JsonObject();
        foreach (var e in locresFile.AllEntries)
        {
            obj[e.Key] = string.IsNullOrEmpty(e.TranslatedText) ? e.SourceText : e.TranslatedText;
        }

        var json = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return Encoding.UTF8.GetBytes(json);
    }

    // ─── CSV / Delimited Ayrıştırma ──────────────────────────────────────────

    private static LocresFile ParseDelimitedText(string content)
    {
        var locres = new LocresFile { Version = LocresVersion.V3 };
        var ns = new LocresNamespace { Name = "Table" };
        locres.Namespaces.Add(ns);

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            string key, text;
            if (line.Contains('='))
            {
                var parts = line.Split('=', 2);
                key = parts[0].Trim();
                text = parts[1].Trim();
            }
            else if (line.Contains('\t'))
            {
                var parts = line.Split('\t', 2);
                key = parts[0].Trim();
                text = parts[1].Trim();
            }
            else if (line.Contains(','))
            {
                var parts = line.Split(',', 2);
                key = parts[0].Trim();
                text = parts[1].Trim();
            }
            else
            {
                continue;
            }

            if (!string.IsNullOrEmpty(key))
            {
                ns.Entries.Add(new LocalizationEntry
                {
                    Key = key,
                    SourceText = text,
                    TranslatedText = text,
                    NamespaceName = ns.Name
                });
            }
        }

        return locres;
    }

    private static byte[] SerializeToCsv(LocresFile locresFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Key,Source,Translation");
        foreach (var e in locresFile.AllEntries)
        {
            var tr = string.IsNullOrEmpty(e.TranslatedText) ? e.SourceText : e.TranslatedText;
            sb.AppendLine($"\"{EscapeCsv(e.Key)}\",\"{EscapeCsv(e.SourceText)}\",\"{EscapeCsv(tr)}\"");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] SerializeToTxt(LocresFile locresFile)
    {
        var sb = new StringBuilder();
        foreach (var e in locresFile.AllEntries)
        {
            var tr = string.IsNullOrEmpty(e.TranslatedText) ? e.SourceText : e.TranslatedText;
            sb.AppendLine($"{e.Key}={tr}");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static LocresFile ParsePlainLines(string content)
    {
        var locres = new LocresFile { Version = LocresVersion.V3 };
        var ns = new LocresNamespace { Name = "Lines" };
        locres.Namespaces.Add(ns);

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        int entryIndex = 1;
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = SanitizeXmlString(lines[i]).Trim();
            if (string.IsNullOrEmpty(raw)) continue;

            // Larian separator'larını temizle, sonra değerlendir
            var line = CleanSolidText(raw);
            if (string.IsNullOrEmpty(line)) continue;
            if (IsXmlNoise(line)) continue;
            if (!line.Any(char.IsLetterOrDigit)) continue;

            // Harf oranı %15 altında olan satırları atla (XML attribute kalıntıları)
            int letterCount = line.Count(char.IsLetter);
            double letterRatio = (double)letterCount / line.Length;
            if (letterRatio < 0.15 && line.Length > 8)
                continue;

            ns.Entries.Add(new LocalizationEntry
            {
                Key = $"line_{entryIndex++}",
                SourceText = line,
                TranslatedText = line,
                NamespaceName = ns.Name
            });
        }

        return locres;
    }

    private static bool IsBinaryBuffer(byte[] buffer)
    {
        if (buffer.Length == 0) return false;

        // UTF-16 LE / BE BOM kontrolü (0xFF 0xFE veya 0xFE 0xFF)
        if (buffer.Length >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
        {
            return false;
        }

        // UTF-8 BOM (0xEF 0xBB 0xBF)
        int start = 0;
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            start = 3;
        }

        int nonPrintable = 0;
        for (int i = start; i < buffer.Length; i++)
        {
            byte b = buffer[i];
            if (b == 0)
            {
                return true; // Metin dosyalarında null byte bulunamaz
            }
            if (b < 0x08 || (b > 0x0D && b < 0x20) || b == 0x7F)
            {
                nonPrintable++;
            }
        }

        return buffer.Length > start && ((double)nonPrintable / (buffer.Length - start) > 0.15);
    }

    private static string EscapeCsv(string? str) => (str ?? string.Empty).Replace("\"", "\"\"");
}
