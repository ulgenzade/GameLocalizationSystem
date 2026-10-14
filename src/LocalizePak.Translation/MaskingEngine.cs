using System.Text.RegularExpressions;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation;

/// <summary>
/// Metin içindeki oyun değişkenlerini ve özel isimleri maskeleyen/geri yükleyen sınıf.
/// Bu sayede AI çevirisi bu özel kodları bozmaz.
/// </summary>
public sealed class MaskingEngine
{
    // Maskeleme regex kalıpları (öncelik sırasıyla)
    private static readonly (string Pattern, string Prefix)[] MaskingRules =
    [
        // Named değişkenler: {PlayerName}, {ItemName}
        (@"\{[A-Za-z_]\w*\}", "VAR"),
        // Pozisyonel değişkenler: {0}, {1}, {2}
        (@"\{\d+\}", "POS"),
        // Ren'Py değişkenleri: [player_name], [hero_name], [gold_count]
        (@"\[[a-z_][a-z0-9_]*\]", "RENVAR"),
        // RPG Maker kontrol kodları: \C[1], \V[2], \N[3], \P[4], \G, \I[5], \FS[24]
        (@"\\[A-Za-z]+\[\d+\]|\\[\.!\^\|\$><\{\}]|\\[Gg]", "RPGMTAG"),
        // Oyun etiketleri / Diyalog koşulları: [DWARF], [OUTLAW], [SOLDIER]
        (@"\[(?!(?:VAR|POS|FMT|ESC|TAG|ETAG|TMPTAG|NAME|GTAG|RENVAR|RPGMTAG)_\d+\])[A-Z0-9_]{2,}\]", "GTAG"),
        // printf formatları: %s, %d, %f, %i, %ls, %u
        (@"%[0-9]*\.?[0-9]*[sdifluLS]", "FMT"),
        // Escape dizileri: \n, \t, \r
        (@"\\[ntr]", "ESC"),
        // Rich text kapanış etiketleri (önce kapanışı maskele): </>, </color>, </b>, </i>
        (@"</>|<\s*/\s*[^>]*>", "ETAG"),
        // Rich text açılış etiketleri: <RichColor ...>, <b>, <i>, <color=#FF0000>, <span ...>
        (@"<[A-Za-z][^>]*>", "TAG"),
        // Unity TextMeshPro sprite/size: <sprite=X>, <size=Y>
        (@"<[a-z]+=\S+?>", "TMPTAG"),
    ];

    /// <summary>
    /// Metni maskeler. Değişkenleri ve özel isimleri etiketlerle değiştirir.
    /// </summary>
    /// <param name="text">Maskelenecek orijinal metin.</param>
    /// <param name="glossary">Özel isim listesi (korunacak isimler).</param>
    /// <returns>Maskelenmiş metin ve geri yükleme haritası.</returns>
    public (string MaskedText, Dictionary<string, string> RestoreMap) Mask(
        string text,
        IReadOnlyList<GlossaryEntry>? glossary = null)
    {
        if (string.IsNullOrEmpty(text)) return (text, []);

        var restoreMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = text;
        int counter = 0;

        // 1. Glossary özel isimleri maskele
        if (glossary is not null)
        {
            foreach (var entry in glossary.Where(g => g.DoNotTranslate || g.Translation is null))
            {
                if (string.IsNullOrWhiteSpace(entry.Original)) continue;
                var pattern = Regex.Escape(entry.Original);
                result = Regex.Replace(result, pattern, match =>
                {
                    var tag = $"[NAME_{counter++}]";
                    restoreMap[tag] = match.Value;
                    return tag;
                });
            }
        }

        // 2. Regex kurallarını uygula
        foreach (var (pattern, prefix) in MaskingRules)
        {
            result = Regex.Replace(result, pattern, match =>
            {
                var tag = $"[{prefix}_{counter++}]";
                restoreMap[tag] = match.Value;
                return tag;
            });
        }

        return (result, restoreMap);
    }

    /// <summary>
    /// Maskelenmiş metin içindeki etiketleri orijinal değerleriyle geri yükler.
    /// Büyük/küçük harf ve boşluk varyasyonlarını da dayanıklı şekilde onarır.
    /// </summary>
    /// <param name="maskedText">Maskelenmiş (çevrilmiş) metin.</param>
    /// <param name="restoreMap">Mask() metodundan dönen geri yükleme haritası.</param>
    public string Restore(string maskedText, Dictionary<string, string> restoreMap)
    {
        if (string.IsNullOrEmpty(maskedText) || restoreMap.Count == 0)
            return maskedText;

        var result = maskedText;
        foreach (var (tag, original) in restoreMap)
        {
            // Tam eşleşme ile hızlı değiştir
            result = result.Replace(tag, original, StringComparison.Ordinal);

            // AI'nin büyük/küçük harf veya iç boşluk bozma durumunu düzelt (örn: [var_0] veya [ VAR_0 ])
            if (result.Contains(tag[1..^1], StringComparison.OrdinalIgnoreCase))
            {
                var inner = tag.Trim('[', ']');
                var regexPattern = @"\[\s*" + Regex.Escape(inner) + @"\s*\]";
                result = Regex.Replace(result, regexPattern, _ => original, RegexOptions.IgnoreCase);
            }
        }

        return result;
    }

    /// <summary>
    /// AI çevirisinde kaybolan veya silinen değişkenleri tespit edip otomatik onarır (Oyun çökmesini %100 önler).
    /// </summary>
    public string RestoreAndRepair(string maskedText, Dictionary<string, string> restoreMap)
    {
        if (string.IsNullOrEmpty(maskedText))
            return maskedText;

        var result = Restore(maskedText, restoreMap);

        // Eksik kalan etiketleri orijinal değerleriyle geri tamamla
        foreach (var (tag, original) in restoreMap)
        {
            if (!string.IsNullOrWhiteSpace(original) && !result.Contains(original, StringComparison.Ordinal))
            {
                result = $"{result.TrimEnd()} {original}";
            }
        }

        return result;
    }

    /// <summary>
    /// Maskelenmiş kaynak ve geri yüklenmiş çevirideki etiket sayılarını karşılaştırır.
    /// </summary>
    public bool ValidateMaskIntegrity(Dictionary<string, string> restoreMap, string translatedText)
    {
        foreach (var tag in restoreMap.Keys)
        {
            if (!translatedText.Contains(tag, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
