using System.Text.RegularExpressions;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation.Services;

public enum MqmCategory
{
    Accuracy,     // Doğruluk (Eksik değişken, çevrilmemiş metin)
    Fluency,      // Akıcılık (Dilbilgisi, eksik kaçış kodları)
    Terminology,  // Terminoloji (Özel isim/sözlük uyumsuzluğu)
    Design        // Biçim/Arayüz (Kapanmayan etiketler, buton taşma riski)
}

public enum QaSeverity
{
    Critical, // Oyun çökme veya kod bozulma riski (Değişkenler, etiketler)
    High,     // Anlam bozulması veya çevrilmemiş metin
    Medium,   // Arayüz taşma riski (%80+ uzama)
    Low       // Boşluk veya küçük biçim uyuşmazlığı
}

public enum QaIssueType
{
    MissingVariable,
    UnclosedTag,
    MissingEscape,
    TrailingWhitespaceMismatch,
    OverflowRisk,
    TerminologyMismatch,
    UntranslatedContent
}

public sealed record QaIssue(
    string EntryKey,
    string NamespaceName,
    string SourceText,
    string TranslatedText,
    QaIssueType IssueType,
    string Description,
    bool CanAutoFix,
    MqmCategory Category = MqmCategory.Accuracy,
    QaSeverity Severity = QaSeverity.Medium
);

/// <summary>
/// Oyun yerelleştirmesi için Otomatik Kalite Kontrol (QA) ve Etiket/Değişken Doğrulama Motoru.
/// Eksik format belirteçlerini, kapanmayan tag'leri ve kaçış karakterlerini tespit edip onarır.
/// </summary>
public static class TranslationQaService
{
    private static readonly Regex VariableRegex = new(@"\{[A-Za-z0-9_]+\}", RegexOptions.Compiled);
    private static readonly Regex PrintfRegex = new(@"%[0-9]*\.?[0-9]*[sdifluLS]", RegexOptions.Compiled);
    private static readonly Regex EscapeRegex = new(@"\\[ntr]", RegexOptions.Compiled);
    private static readonly Regex OpenHtmlTagRegex = new(@"<([A-Za-z][^>/]*)>", RegexOptions.Compiled);
    private static readonly Regex CloseHtmlTagRegex = new(@"</([A-Za-z][^>]*)>", RegexOptions.Compiled);

    /// <summary>
    /// Verilen girdiyi QA kurallarına göre doğrular ve tespit edilen sorunları döndürür.
    /// </summary>
    public static List<QaIssue> ValidateEntry(LocalizationEntry entry)
    {
        var issues = new List<QaIssue>();
        var src = entry.SourceText;
        var tr = entry.TranslatedText;

        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(tr))
            return issues;

        // 1. Süslü Parantez Değişkenleri: {0}, {PlayerName}, vb.
        var srcVars = VariableRegex.Matches(src).Select(m => m.Value).Distinct().ToList();
        foreach (var v in srcVars)
        {
            if (!tr.Contains(v, StringComparison.Ordinal))
            {
                issues.Add(new QaIssue(
                    entry.Key,
                    entry.NamespaceName,
                    src,
                    tr,
                    QaIssueType.MissingVariable,
                    $"Kaynak metindeki '{v}' değişkeni çeviride bulunamadı.",
                    CanAutoFix: true,
                    Category: MqmCategory.Accuracy,
                    Severity: QaSeverity.Critical
                ));
            }
        }

        // 2. Printf Belirteçleri: %s, %d, %f, vb.
        var srcPrintfs = PrintfRegex.Matches(src).Select(m => m.Value).Distinct().ToList();
        foreach (var p in srcPrintfs)
        {
            if (!tr.Contains(p, StringComparison.Ordinal))
            {
                issues.Add(new QaIssue(
                    entry.Key,
                    entry.NamespaceName,
                    src,
                    tr,
                    QaIssueType.MissingVariable,
                    $"Kaynak metindeki '{p}' format kodu çeviride bulunamadı.",
                    CanAutoFix: true,
                    Category: MqmCategory.Accuracy,
                    Severity: QaSeverity.Critical
                ));
            }
        }

        // 3. Kaçış Karakterleri: \n, \t, \r
        var srcEscapes = EscapeRegex.Matches(src).Select(m => m.Value).Distinct().ToList();
        foreach (var esc in srcEscapes)
        {
            if (!tr.Contains(esc, StringComparison.Ordinal))
            {
                issues.Add(new QaIssue(
                    entry.Key,
                    entry.NamespaceName,
                    src,
                    tr,
                    QaIssueType.MissingEscape,
                    $"Kaynak metindeki '{esc}' satır/sekme kaçış kodu çeviride eksik.",
                    CanAutoFix: true,
                    Category: MqmCategory.Fluency,
                    Severity: QaSeverity.Medium
                ));
            }
        }

        // 4. Kapanmayan HTML / RichText Etiketleri
        var openTags = OpenHtmlTagRegex.Matches(tr);
        var closeTags = CloseHtmlTagRegex.Matches(tr);
        if (openTags.Count > closeTags.Count)
        {
            issues.Add(new QaIssue(
                entry.Key,
                entry.NamespaceName,
                src,
                tr,
                QaIssueType.UnclosedTag,
                $"Çeviride {openTags.Count} açılış etiketine karşılık {closeTags.Count} kapanış etiketi var (Kapanmayan etiketler oyun arayüzünü bozabilir).",
                CanAutoFix: true,
                Category: MqmCategory.Design,
                Severity: QaSeverity.Critical
            ));
        }

        // 5. Kısa UI Buton / Arayüz Metinlerinde Taşma (Overflow) Riski
        if (src.Length <= 25 && tr.Length > (int)(src.Length * 1.8) && (tr.Length - src.Length) >= 8)
        {
            int diffPercent = (int)(((tr.Length - src.Length) / (double)src.Length) * 100);
            issues.Add(new QaIssue(
                entry.Key,
                entry.NamespaceName,
                src,
                tr,
                QaIssueType.OverflowRisk,
                $"Buton/Arayüz metni taşma riski: Metin %{diffPercent} uzadı ({src.Length} -> {tr.Length} karaktere çıktı).",
                CanAutoFix: false,
                Category: MqmCategory.Design,
                Severity: QaSeverity.Medium
            ));
        }

        // 6. Çevrilmemiş İçerik (Untranslated Content)
        if (src.Length >= 8 && src.Equals(tr, StringComparison.Ordinal) && src.Any(char.IsLetter))
        {
            issues.Add(new QaIssue(
                entry.Key,
                entry.NamespaceName,
                src,
                tr,
                QaIssueType.UntranslatedContent,
                "Metin kaynak dilde kalmış görünüyor (Kaynak ve çeviri metinleri birebir aynı).",
                CanAutoFix: false,
                Category: MqmCategory.Accuracy,
                Severity: QaSeverity.High
            ));
        }

        return issues;
    }

    /// <summary>
    /// Girdideki eksik değişkenleri ve kapanmayan etiketleri otomatik onarır.
    /// </summary>
    /// <returns>Düzeltme yapıldıysa true, gerekmediyse false.</returns>
    public static bool AutoFixEntry(LocalizationEntry entry)
    {
        var src = entry.SourceText;
        var tr = entry.TranslatedText;

        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(tr))
            return false;

        bool modified = false;
        var fixedText = tr;

        // 1. Eksik değişkenleri ({0}, {Name}, %s) metnin sonuna ekle
        var srcVars = VariableRegex.Matches(src).Select(m => m.Value).Distinct();
        foreach (var v in srcVars)
        {
            if (!fixedText.Contains(v, StringComparison.Ordinal))
            {
                fixedText = $"{fixedText.TrimEnd()} {v}";
                modified = true;
            }
        }

        var srcPrintfs = PrintfRegex.Matches(src).Select(m => m.Value).Distinct();
        foreach (var p in srcPrintfs)
        {
            if (!fixedText.Contains(p, StringComparison.Ordinal))
            {
                fixedText = $"{fixedText.TrimEnd()} {p}";
                modified = true;
            }
        }

        // 2. Eksik \n kaçışları
        var srcNewlineCount = src.Split(@"\n").Length - 1;
        var trNewlineCount = fixedText.Split(@"\n").Length - 1;
        if (srcNewlineCount > trNewlineCount && src.EndsWith(@"\n") && !fixedText.EndsWith(@"\n"))
        {
            fixedText += @"\n";
            modified = true;
        }

        // 3. Kapanmayan etiketler için basit tamamlama
        if (fixedText.Contains("<color", StringComparison.OrdinalIgnoreCase) && !fixedText.Contains("</color>", StringComparison.OrdinalIgnoreCase))
        {
            fixedText += "</color>";
            modified = true;
        }
        if (fixedText.Contains("<b>", StringComparison.OrdinalIgnoreCase) && !fixedText.Contains("</b>", StringComparison.OrdinalIgnoreCase))
        {
            fixedText += "</b>";
            modified = true;
        }
        if (fixedText.Contains("<i>", StringComparison.OrdinalIgnoreCase) && !fixedText.Contains("</i>", StringComparison.OrdinalIgnoreCase))
        {
            fixedText += "</i>";
            modified = true;
        }

        if (modified)
        {
            entry.TranslatedText = fixedText;
            entry.HasVariableError = false;
            if (entry.Status == TranslationStatus.Error)
            {
                entry.Status = TranslationStatus.Done;
            }
            entry.Notes = "✅ QA Motoru tarafından eksik etiketler otomatik onarıldı.";
        }

        return modified;
    }
}
