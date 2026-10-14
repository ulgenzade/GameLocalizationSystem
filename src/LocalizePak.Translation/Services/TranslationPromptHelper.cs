using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation.Services;

/// <summary>
/// Tüm AI sağlayıcıları (Gemini, Claude, OpenAI, DeepSeek vb.) için kaynak ve hedef dili
/// kesin bir dille bildiren, tutarlı ve profesyonel prompt inşa yardımcısı.
/// </summary>
public static class TranslationPromptHelper
{
    public static string BuildSystemPrompt(TranslationContext ctx, string outputFormatDescription)
    {
        var sb = new StringBuilder();

        var srcName = string.IsNullOrWhiteSpace(ctx.SourceLanguageName) ? "İngilizce" : ctx.SourceLanguageName;
        var tgtName = string.IsNullOrWhiteSpace(ctx.TargetLanguageName) ? "Türkçe" : ctx.TargetLanguageName;
        var isAutoSource = ctx.SourceLanguage == "auto" || srcName.Contains("Otomatik", StringComparison.OrdinalIgnoreCase);

        sb.AppendLine($"🎯 HEDEF DİL: {tgtName} ({ctx.TargetLanguage})");
        if (isAutoSource)
        {
            sb.AppendLine("KAYNAK DİL: Otomatik Algılama (Oyunun Orijinal Dili - Çoğunlukla İngilizce)");
            sb.AppendLine($"MUTLAK GÖREV: Aşağıda verilen tüm kaynak oyun metinlerini KESİNLİKLE {tgtName} diline çevireceksin. Kaynak metinler hangi dilde olursa olsun çıktı metinlerin ASLA kaynak dilde kalamaz; eksiksiz ve akıcı {tgtName} olmalıdır.");
        }
        else
        {
            sb.AppendLine($"KAYNAK DİL: {srcName} ({ctx.SourceLanguage})");
            sb.AppendLine($"MUTLAK GÖREV: Aşağıda verilen tüm oyun metinlerini KESİNLİKLE {tgtName} diline çevireceksin. Kaynak metinler {srcName} olsa dahi çıktı metinlerin ASLA {srcName} kalamaz; eksiksiz ve akıcı {tgtName} olmalıdır.");
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(ctx.SystemPromptTemplate))
        {
            var templ = ctx.SystemPromptTemplate
                .Replace("kaynak dilden hedef dile", isAutoSource ? $"kaynak dilden {tgtName}'ye" : $"{srcName}'den {tgtName}'ye")
                .Replace("hedef dile", $"{tgtName}'ye")
                .Replace("hedef dilde", $"{tgtName}'de");
            sb.AppendLine(templ);
        }
        else
        {
            sb.AppendLine("Sen dünyanın en iyi profesyonel video oyunu yerelleştirme (Localization) uzmanısın.");
            sb.AppendLine(isAutoSource
                ? $"Görevin: Oyun metinlerini kaynak dilden {tgtName}'ye en doğal, edebi ve kaliteli şekilde çevirmek."
                : $"Görevin: Oyun metinlerini {srcName}'den {tgtName}'ye en doğal, edebi ve kaliteli şekilde çevirmek.");
            sb.AppendLine();
            sb.AppendLine("MUTLAK KURALLAR:");
            sb.AppendLine("1. [VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [ETAG_X], [TMPTAG_X], [NAME_X], [GTAG_X], [RENVAR_X], [RPGMTAG_X] etiketlerini ASLA çevirme, silme veya sırasını bozma.");
            sb.AppendLine("2. Bu etiketleri tam olarak aynı şekilde, değiştirmeden çeviride koru.");
            sb.AppendLine($"3. Başlıklarda, diyaloglarda ve '*' ile başlayan anlatıcı aksiyonlarında metinleri {tgtName}'ye çevir.");
        }

        sb.AppendLine();
        sb.AppendLine(outputFormatDescription);

        if (!string.IsNullOrWhiteSpace(ctx.UserDirective))
        {
            sb.AppendLine();
            sb.AppendLine($"ÖZEL OYUN DİREKTİFİ:\n{ctx.UserDirective}");
        }

        if (ctx.Glossary != null && ctx.Glossary.Any())
        {
            sb.AppendLine();
            sb.AppendLine("ÇEVİRİLMEYECEK VEYA ÖZEL ÇEVİRİSİ OLAN İSİMLER:");
            foreach (var entry in ctx.Glossary)
                sb.AppendLine($"  - {entry}");
        }

        return sb.ToString();
    }

    public static string BuildUserJsonArrayPrompt(IReadOnlyList<string> texts, TranslationContext ctx)
    {
        var tgtName = string.IsNullOrWhiteSpace(ctx.TargetLanguageName) ? "Türkçe" : ctx.TargetLanguageName;
        var items = texts.Select((t, i) => new JsonObject { ["id"] = i, ["text"] = t });
        var arr = new JsonArray(items.Select(o => (JsonNode)o).ToArray());
        return $"Aşağıdaki {texts.Count} adet oyun metnini KESİNLİKLE {tgtName} ({ctx.TargetLanguage}) diline çevir ve aynı sırada SADECE geçerli bir JSON dizisi olarak döndür ([\"çeviri 1\", \"çeviri 2\", ...]):\n{arr.ToJsonString()}";
    }

    public static string BuildUserJsonObjectPrompt(IReadOnlyList<string> texts, TranslationContext ctx)
    {
        var tgtName = string.IsNullOrWhiteSpace(ctx.TargetLanguageName) ? "Türkçe" : ctx.TargetLanguageName;
        var arr = new JsonArray(texts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        return $"Aşağıdaki {texts.Count} adet oyun metnini KESİNLİKLE {tgtName} ({ctx.TargetLanguage}) diline çevir ve aynı sırada {{\"translations\": [\"çeviri 1\", \"çeviri 2\", ...]}} formatında bir JSON nesnesi olarak döndür:\n{arr.ToJsonString()}";
    }

    /// <summary>
    /// Görünen dil adını veya karma metinleri (örn. "English (İngilizce)", "Otomatik Algıla") ISO-639 koduna dönüştürür.
    /// Türkçe karakter casing (İ -> i, ı -> i) sorunlarına karşı tam dayanıklıdır.
    /// </summary>
    public static string MapLanguageToIsoCode(string? languageName)
    {
        if (string.IsNullOrWhiteSpace(languageName)) return "tr";
        var norm = languageName.Trim()
            .Replace("İ", "i")
            .Replace("I", "i")
            .Replace("ı", "i")
            .Replace("ç", "c")
            .Replace("Ç", "c")
            .Replace("ş", "s")
            .Replace("Ş", "s")
            .Replace("ü", "u")
            .Replace("Ü", "u")
            .Replace("ö", "o")
            .Replace("Ö", "o")
            .Replace("ğ", "g")
            .Replace("Ğ", "g")
            .ToLowerInvariant();

        if (norm.Contains("otomatik") || norm.Contains("auto")) return "auto";
        if (norm.Contains("turk") || norm == "tr") return "tr";
        if (norm.Contains("ingiliz") || norm.Contains("english") || norm == "en") return "en";
        if (norm.Contains("rus") || norm == "ru") return "ru";
        if (norm.Contains("cin") || norm.Contains("chin") || norm.Contains("中文") || norm == "zh") return "zh";
        if (norm.Contains("alman") || norm.Contains("german") || norm == "de") return "de";
        if (norm.Contains("fransiz") || norm.Contains("french") || norm == "fr") return "fr";
        if (norm.Contains("ispanyol") || norm.Contains("spanish") || norm == "es") return "es";
        if (norm.Contains("japon") || norm.Contains("japan") || norm == "ja") return "ja";
        if (norm.Contains("kore") || norm.Contains("korean") || norm == "ko") return "ko";
        if (norm.Contains("italyan") || norm.Contains("italian") || norm == "it") return "it";
        if (norm.Contains("portekiz") || norm.Contains("portuguese") || norm == "pt") return "pt";
        if (norm.Contains("arap") || norm.Contains("arabic") || norm == "ar") return "ar";

        return "tr";
    }
}

