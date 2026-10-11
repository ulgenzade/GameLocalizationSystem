using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation.Services;

/// <summary>
/// Yapay zekaya oyun adını, yapımcısını ve çıkış yılını ileterek oyun motorunu ve bilinen AES anahtarını araştıran servis.
/// </summary>
public sealed class AiGameDetectorService
{
    public async Task<GameEngineAndAesResult> DetectEngineAndAesKeyAsync(
        string gameName,
        string developerStudio,
        string releaseYear,
        string? technicalEngineHint,
        ITranslationProvider provider,
        CancellationToken ct = default)
    {
        var prompt = $$"""
        GÖREV:
        Bir oyuncu bir oyunun .pak dosyasını yamalamak istiyor ve dosyanın şifreli olup olmadığını/hangi motorla yapıldığını öğrenmeye çalışıyor.
        
        KULLANICI BİLGİLERİ:
        - OYUN ADI: {{gameName}}
        - GELİŞTİRİCİ / STÜDYO: {{(string.IsNullOrWhiteSpace(developerStudio) ? "Bilinmiyor" : developerStudio)}}
        - ÇIKIŞ YILI: {{(string.IsNullOrWhiteSpace(releaseYear) ? "Bilinmiyor" : releaseYear)}}
        - MOTOR İPUCU: {{(string.IsNullOrWhiteSpace(technicalEngineHint) ? "Bilinmiyor" : technicalEngineHint)}}

        LÜTFEN ANALİZ ET:
        1. Bu oyun hangi oyun motoru ile yapılmıştır? (Örn: Unreal Engine 5.2, Unreal Engine 4.27, Larian Engine, CryEngine vb.)
        2. Bu oyunun modlama topluluğunda (CS.RIN.RU, FModel, UModel) bilinen bir AES-256 anahtarı var mı? (Varsa 0x ile başlayan 64 karakterlik kodu 'aes_key' alanına yaz, yoksa null bırak).
        3. 'search_advice' alanında teknik jargona boğmadan genel kullanıcının anlayacağı dille kısa bir özet yaz:
           Örnek: "Bu oyun Unreal Engine 5 ile yapılmıştır. Eğer paket şifreliyse, aşağıdaki 'Google'da Ara' butonu ile CS.RIN.RU topluluğunda paylaşılan şifreyi bulabilirsiniz."
        4. 'search_query' alanına Google için en etkili arama sorgusunu oluştur (Örn: "{{gameName}} AES key CS.RIN.RU").

        YANIT FORMATI (SADECE GEÇERLİ JSON):
        {
          "engine_name": "Unreal Engine 5.3",
          "engine_version": "5.3",
          "aes_key": "0x1234... (veya null)",
          "search_advice": "Bu oyun Unreal Engine 5 ile yapılmıştır...",
          "search_query": "{{gameName}} Unreal Engine AES key"
        }
        """;

        var dummyContext = new TranslationContext
        {
            UserDirective = "Sen oyun motorları, oyun mimarileri ve tersine mühendislik uzmanısın. Sade, net ve anlaşılır bilgi ver.",
            TargetLanguage = "tr",
            TargetLanguageName = "Türkçe"
        };

        var responseList = await provider.TranslateBatchAsync([prompt], dummyContext, ct);
        var rawResponse = responseList.FirstOrDefault() ?? "{}";

        return ParseResponse(gameName, developerStudio, rawResponse);
    }

    private static GameEngineAndAesResult ParseResponse(string gameName, string dev, string raw)
    {
        var result = new GameEngineAndAesResult
        {
            GameName = gameName,
            Developer = dev,
            SearchQuery = $"{gameName} AES key CS.RIN.RU"
        };

        try
        {
            raw = raw.Trim();
            if (raw.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) raw = raw[7..];
            if (raw.StartsWith("```", StringComparison.OrdinalIgnoreCase)) raw = raw[3..];
            if (raw.EndsWith("```", StringComparison.OrdinalIgnoreCase)) raw = raw[..^3];
            raw = raw.Trim();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("engine_name", out var engProp))
                result.EngineName = engProp.GetString() ?? "Unreal Engine";

            if (root.TryGetProperty("engine_version", out var verProp))
                result.EngineVersion = verProp.GetString() ?? "";

            if (root.TryGetProperty("aes_key", out var keyProp) && keyProp.ValueKind == JsonValueKind.String)
            {
                var keyStr = keyProp.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(keyStr) && keyStr.Length >= 32 && !keyStr.Contains("null", StringComparison.OrdinalIgnoreCase))
                    result.FoundAesKey = keyStr;
            }

            if (root.TryGetProperty("search_advice", out var advProp))
                result.SearchAdvice = advProp.GetString() ?? "";

            if (root.TryGetProperty("search_query", out var queryProp))
                result.SearchQuery = queryProp.GetString() ?? result.SearchQuery;
        }
        catch
        {
            result.EngineName = "Unreal Engine";
            result.SearchAdvice = $"Bu oyun için CS.RIN.RU veya FModel üzerinde '{gameName} AES key' araması yapabilirsiniz.";
        }

        if (string.IsNullOrWhiteSpace(result.SearchAdvice))
        {
            result.SearchAdvice = $"Bu oyun {result.EngineName} ile geliştirilmiştir. Eğer paket şifreli ise aşağıdaki butondan hazır topluluk anahtarını aratabilirsiniz.";
        }

        return result;
    }
}
