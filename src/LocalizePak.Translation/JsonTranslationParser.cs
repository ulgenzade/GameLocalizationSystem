using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalizePak.Translation;

/// <summary>
/// Tüm LLM / AI modellerinden (Gemini, OpenAI, Claude, DeepSeek, Yerel LLM vb.)
/// gelen JSON yanıtlarını dayanıklı ve hataya toleranslı şekilde ayrıştıran yardımcı sınıf.
/// </summary>
public static class JsonTranslationParser
{
    private static readonly string[] PossibleTextKeys =
    [
        "translation",
        "text",
        "translated_text",
        "translatedText",
        "translated",
        "target",
        "result",
        "value",
        "content"
    ];

    private static readonly string[] PossibleArrayKeys =
    [
        "translations",
        "results",
        "data",
        "texts",
        "items",
        "translated",
        "lines"
    ];

    /// <summary>
    /// Ham AI yanıt metnini ayrıştırıp beklenen sayıda çevrilmiş metin listesi döndürür.
    /// </summary>
    public static IReadOnlyList<string> Parse(string rawResponse, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return Enumerable.Repeat(string.Empty, expectedCount).ToList();

        var cleaned = CleanMarkdown(rawResponse);
        var results = new List<string>(expectedCount);

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                ExtractFromArray(root, results);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Obje içinde dizi ara (örn: {"translations": [...]})
                bool arrayFound = false;
                foreach (var key in PossibleArrayKeys)
                {
                    if (root.TryGetProperty(key, out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
                    {
                        ExtractFromArray(arrayProp, results);
                        arrayFound = true;
                        break;
                    }
                }

                if (!arrayFound)
                {
                    // Herhangi bir Array tipindeki property'yi ara
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            ExtractFromArray(prop.Value, results);
                            arrayFound = true;
                            break;
                        }
                    }
                }

                // Eğer dizi yoksa ama {"0": "çeviri 1", "1": "çeviri 2"} gibi indeksli obje ise
                if (!arrayFound)
                {
                    var dict = new SortedDictionary<int, string>();
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var idx))
                        {
                            var val = prop.Value.ValueKind == JsonValueKind.String
                                ? prop.Value.GetString() ?? string.Empty
                                : prop.Value.ToString();
                            dict[idx] = val;
                        }
                    }

                    if (dict.Count > 0)
                    {
                        for (int i = 0; i < Math.Max(expectedCount, dict.Keys.Max() + 1); i++)
                        {
                            results.Add(dict.TryGetValue(i, out var text) ? text : string.Empty);
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Token kesintisi veya bozuk JSON durumunda Regex Fallback
            results = ExtractWithRegexFallback(cleaned, expectedCount);
        }
        catch
        {
            results = ExtractWithRegexFallback(cleaned, expectedCount);
        }

        // Eksik satırları boş string ile tamamla veya fazlalığı kırp
        while (results.Count < expectedCount)
        {
            results.Add(string.Empty);
        }

        if (results.Count > expectedCount)
        {
            return results.Take(expectedCount).ToList();
        }

        return results;
    }

    private static void ExtractFromArray(JsonElement arrayElement, List<string> results)
    {
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                results.Add(item.GetString() ?? string.Empty);
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                string foundText = string.Empty;
                bool matched = false;

                // Bilinen anahtarları sırayla dene
                foreach (var key in PossibleTextKeys)
                {
                    if (item.TryGetProperty(key, out var prop))
                    {
                        foundText = prop.ValueKind == JsonValueKind.String
                            ? prop.GetString() ?? string.Empty
                            : prop.ToString();
                        matched = true;
                        break;
                    }
                }

                // Bilinen anahtar yoksa objedeki ilk string özelliği al
                if (!matched)
                {
                    foreach (var prop in item.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            foundText = prop.Value.GetString() ?? string.Empty;
                            break;
                        }
                    }
                }

                results.Add(foundText);
            }
            else
            {
                results.Add(item.ToString());
            }
        }
    }

    private static List<string> ExtractWithRegexFallback(string text, int expectedCount)
    {
        var results = new List<string>();

        // 1. Durum: "translation": "..." veya "text": "..." kalıplarını yakala
        var propMatches = Regex.Matches(text, @"""(?:translation|text|translated_text|translatedText|target|result|value)""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);
        if (propMatches.Count > 0)
        {
            foreach (Match m in propMatches)
            {
                results.Add(UnescapeJsonString(m.Groups[1].Value));
            }
            return results;
        }

        // 2. Durum: Dizi içindeki tırnaklı stringleri yakala: [ "...", "..." ]
        var arrayMatches = Regex.Matches(text, @"""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);
        foreach (Match m in arrayMatches)
        {
            var val = m.Groups[1].Value;
            // JSON anahtarı değilse (id, text, translation gibi kısa anahtarları ele)
            if (!PossibleTextKeys.Contains(val, StringComparer.OrdinalIgnoreCase) &&
                !PossibleArrayKeys.Contains(val, StringComparer.OrdinalIgnoreCase) &&
                val != "id")
            {
                results.Add(UnescapeJsonString(val));
            }
        }

        return results;
    }

    private static string CleanMarkdown(string input)
    {
        var cleaned = input.Trim();

        // <think>...</think> düşünme / reasoning bloklarını ayıkla
        cleaned = Regex.Replace(cleaned, @"(?s)<think>.*?</think>", string.Empty).Trim();

        // ```json ... ``` bloklarını temizle
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[3..];

        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^3];

        return cleaned.Trim();
    }

    private static string UnescapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        try
        {
            // Basit JSON unescape
            return Regex.Unescape(value);
        }
        catch
        {
            return value
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\\", "\\");
        }
    }
}
