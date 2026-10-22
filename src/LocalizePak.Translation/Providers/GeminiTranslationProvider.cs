using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.Translation.Services;

namespace LocalizePak.Translation.Providers;

/// <summary>
/// Google Gemini Flash API entegrasyonu.
/// Batch çevirisi için JSON formatını kullanır.
/// Sistem talimatı (kurallar + direktif/lore + sözlük) yeterince büyükse explicit context caching ile
/// bir kez <c>cachedContents</c> olarak oluşturulur ve oturumdaki tüm batch'ler bu önbelleği kullanır.
/// Küçük istemlerde Gemini'nin otomatik (implicit) önbelleği, sabit tutulan istem öneki sayesinde devrede kalır.
/// </summary>
public sealed class GeminiTranslationProvider : ITranslationProvider, IPromptCacheReporter
{
    private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Explicit önbellek bu tahmini token eşiğinin altında denenmez (Gemini 3.x/4.x minimumu: 4.096).</summary>
    public const int MinExplicitCacheTokens = 4096;

    /// <summary>Önbellek ömrü; süre dolmadan <see cref="CacheRenewMargin"/> kala yenisi oluşturulur.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CacheRenewMargin = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private string _apiKey = string.Empty;
    private string _model = "gemini-3.5-flash-lite";

    // Aktif explicit önbellek (paralel batch işçileri arasında paylaşılır)
    private string? _cacheName;
    private string? _cacheKey;
    private DateTime _cacheExpiresUtc;
    // Bu model + istem için önbellek oluşturma reddedildiyse tekrar denenmez
    private string? _cacheRejectedKey;

    public string Name => "Gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);
    public PromptCacheStats CacheStats { get; } = new();

    /// <summary>Saat kaynağı (testlerde süre dolumunu simüle etmek için değiştirilebilir).</summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public GeminiTranslationProvider(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public void Configure(string apiKey, string model = "gemini-3.5-flash-lite")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gemini-3.5-flash-lite" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini API anahtarı yapılandırılmamış.");

        var systemInstruction = BuildSystemInstruction(context);
        var userPrompt = BuildUserPrompt(maskedTexts, context);
        var effort = context.NormalizedReasoningEffort;

        var cacheName = await GetOrCreateCacheAsync(systemInstruction, ct);
        var requestBody = BuildRequestBody(_model, systemInstruction, userPrompt, effort, cacheName);

        var (response, responseBody) = await PostGenerateAsync(_model, requestBody, ct);

        // Önbellekli istek reddedildiyse aynı batch'i önbelleksiz (inline) tekrar gönder:
        // - Süresi dolmuş / silinmiş önbellek → önbelleği bırak, sonraki batch yenisini oluşturur.
        // - Diğer 4xx (429 hariç, örn. 400 INVALID_ARGUMENT) → bu oturumda önbelleği kapat ve nedenini raporla.
        //   Böylece önbellek hiçbir durumda çeviriyi bozamaz.
        int status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode && cacheName != null &&
            (IsCacheError(responseBody) || (status >= 400 && status < 500 && status != 429)))
        {
            response.Dispose();
            if (IsCacheError(responseBody))
                await InvalidateCacheAsync(cacheName);
            else
                await DisableCacheAsync(cacheName, systemInstruction, $"Önbellekli istek reddedildi ({status}): {Excerpt(responseBody)}");

            requestBody = BuildRequestBody(_model, systemInstruction, userPrompt, effort, cachedContentName: null);
            (response, responseBody) = await PostGenerateAsync(_model, requestBody, ct);
        }

        using var _ = response;

        if (!response.IsSuccessStatusCode)
        {
            // Eğer model aşırı yoğun (503), bağlantı koptu veya bulunamadı (404) ise:
            // Anında yüksek hacimli hafif ve garantili yedek modele (gemini-3.5-flash-lite veya gemini-3.1-flash-lite) geçiş yap
            if ((response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                 response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                 response.StatusCode == System.Net.HttpStatusCode.BadGateway))
            {
                var fallbackModel = _model.Equals("gemini-3.5-flash-lite", StringComparison.OrdinalIgnoreCase)
                    ? "gemini-3.1-flash-lite"
                    : "gemini-3.5-flash-lite";

                // Önbellekler modele bağlıdır; yedek model her zaman önbelleksiz (inline) istekle denenir.
                var fallbackBody = BuildRequestBody(fallbackModel, systemInstruction, userPrompt, effort, cachedContentName: null);
                var (fallbackResponse, fallbackResponseBody) = await PostGenerateAsync(fallbackModel, fallbackBody, ct);
                using var __ = fallbackResponse;

                if (fallbackResponse.IsSuccessStatusCode)
                {
                    _model = fallbackModel;
                    RecordCacheUsage(fallbackResponseBody, CacheStats);
                    return ParseResponse(fallbackResponseBody, maskedTexts.Count);
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || responseBody.Contains("RESOURCE_EXHAUSTED"))
            {
                throw new HttpRequestException(
                    $"⚠️ Gemini API Kotası Doldu (429 - Günlük İstek Sınırı / Rate Limit). Lütfen Ayarlar'dan modeli değiştirin (örn. gemini-3.5-flash-lite) veya başka bir API anahtarı ekleyin. Ayrıntı: {Excerpt(responseBody.Replace(_apiKey, "***"))}",
                    null,
                    System.Net.HttpStatusCode.TooManyRequests);
            }

            var safeErrorMessage = responseBody.Replace(_apiKey, "***");
            throw new HttpRequestException($"Gemini API hatası ({response.StatusCode}): {safeErrorMessage}", null, response.StatusCode);
        }

        RecordCacheUsage(responseBody, CacheStats);
        return ParseResponse(responseBody, maskedTexts.Count);
    }

    private async Task<(HttpResponseMessage Response, string Body)> PostGenerateAsync(string model, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return (response, responseBody);
    }

    /// <summary>
    /// generateContent istek gövdesini oluşturur. <paramref name="cachedContentName"/> verildiğinde sistem talimatı
    /// gövdeye EKLENMEZ (önbelleğin içindedir; Gemini ikisinin birlikte gönderilmesini reddeder).
    /// </summary>
    public static JsonObject BuildRequestBody(string model, string systemInstruction, string userPrompt, string effort, string? cachedContentName)
    {
        var genConfig = new JsonObject
        {
            ["temperature"] = 0.2,
            ["maxOutputTokens"] = 8192,
            ["responseMimeType"] = "application/json"
        };

        if (effort is "low" or "medium" or "high")
        {
            int budget = effort switch
            {
                "low" => 1024,
                "medium" => 2048,
                "high" => 4096,
                _ => 1024
            };
            genConfig["thinkingConfig"] = new JsonObject
            {
                ["thinkingBudget"] = budget
            };
        }
        else if (SupportsThinkingOff(model))
        {
            genConfig["thinkingConfig"] = new JsonObject
            {
                ["thinkingBudget"] = 0
            };
        }
        // Gemini 3.x ve sonrası düşünmenin tamamen kapatılmasına izin vermez (thinkingBudget: 0 → 400 INVALID_ARGUMENT);
        // "Kapalı" seçiliyse thinkingConfig hiç gönderilmez ve modelin varsayılan (en hafif) düşünme seviyesi kullanılır.

        var requestBody = new JsonObject();

        if (cachedContentName != null)
        {
            requestBody["cachedContent"] = cachedContentName;
        }
        else
        {
            requestBody["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = systemInstruction }
                }
            };
        }

        requestBody["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = userPrompt }
                }
            }
        };
        requestBody["generationConfig"] = genConfig;
        requestBody["safetySettings"] = new JsonArray
        {
            new JsonObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "BLOCK_NONE" },
            new JsonObject { ["category"] = "HARM_CATEGORY_HATE_SPEECH", ["threshold"] = "BLOCK_NONE" },
            new JsonObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "BLOCK_NONE" },
            new JsonObject { ["category"] = "HARM_CATEGORY_DANGEROUS_CONTENT", ["threshold"] = "BLOCK_NONE" },
            new JsonObject { ["category"] = "HARM_CATEGORY_CIVIC_INTEGRITY", ["threshold"] = "BLOCK_NONE" }
        };

        return requestBody;
    }

    /// <summary>
    /// thinkingBudget: 0 ile düşünme tamamen kapatılabilir mi? Yalnızca Gemini 1.x / 2.x modellerinde.
    /// (Canlı doğrulandı: gemini-3.5-flash-lite budget 0'ı 400 INVALID_ARGUMENT ile reddediyor.)
    /// </summary>
    public static bool SupportsThinkingOff(string model) =>
        model.StartsWith("gemini-1", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("gemini-2", StringComparison.OrdinalIgnoreCase);

    // ─── Explicit Context Caching ────────────────────────────────────────────

    /// <summary>
    /// Model explicit önbelleği destekliyor mu? Gemini 4 Argon ve sonrası yalnızca implicit önbellek kullanır.
    /// </summary>
    public static bool SupportsExplicitCache(string model) =>
        !model.StartsWith("gemini-4", StringComparison.OrdinalIgnoreCase);

    /// <summary>Kaba token tahmini (~4 karakter = 1 token); yalnızca önbellek eşiği kararı için kullanılır.</summary>
    public static int EstimateTokens(string text) => text.Length / 4;

    /// <summary>
    /// Bu model + sistem talimatı için geçerli bir önbellek adı döndürür; gerekirse oluşturur.
    /// Önbellek uygun değilse veya oluşturulamazsa null döner ve istek inline gönderilir.
    /// </summary>
    private async Task<string?> GetOrCreateCacheAsync(string systemInstruction, CancellationToken ct)
    {
        if (!SupportsExplicitCache(_model) || EstimateTokens(systemInstruction) < MinExplicitCacheTokens)
            return null;

        var key = CacheKey(systemInstruction);
        if (key == _cacheRejectedKey)
            return null;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cacheName != null && _cacheKey == key && UtcNow() < _cacheExpiresUtc - CacheRenewMargin)
                return _cacheName;

            if (key == _cacheRejectedKey)
                return null;

            var body = new JsonObject
            {
                ["model"] = $"models/{_model}",
                ["systemInstruction"] = new JsonObject
                {
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = systemInstruction } }
                },
                ["ttl"] = $"{(int)CacheTtl.TotalSeconds}s"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/cachedContents");
            request.Headers.Add("x-goog-api-key", _apiKey);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            var name = response.IsSuccessStatusCode ? TryReadCacheName(responseBody) : null;
            if (name == null)
            {
                _cacheName = null;
                _cacheKey = null;

                // Kalıcı ret: 400 "Cached content is too small", model desteklemiyor veya 429
                // (ücretsiz katmanda explicit önbellek kotası 0'dır; tekrar denemek yalnızca istek kotasını tüketir).
                // Yalnızca 5xx sunucu hatalarında sonraki batch yeniden dener.
                int status = (int)response.StatusCode;
                if (status < 500)
                {
                    _cacheRejectedKey = key;
                    CacheStats.RecordCacheDisabled($"Gemini önbellek oluşturmayı reddetti ({status}): {Excerpt(responseBody)}");
                }
                return null;
            }

            _cacheName = name;
            _cacheKey = key;
            _cacheExpiresUtc = UtcNow() + CacheTtl;
            return _cacheName;
        }
        catch (HttpRequestException)
        {
            // Ağ hatası: önbelleksiz devam et, sonraki batch tekrar dener.
            return null;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private string CacheKey(string systemInstruction) => $"{_model}\n{systemInstruction}";

    /// <summary>Önbelleği bu oturum için kalıcı olarak kapatır ve nedenini istatistiğe yazar.</summary>
    private async Task DisableCacheAsync(string cacheName, string systemInstruction, string reason)
    {
        await _cacheLock.WaitAsync();
        try
        {
            _cacheRejectedKey = CacheKey(systemInstruction);
            if (_cacheName == cacheName)
            {
                _cacheName = null;
                _cacheKey = null;
            }
            CacheStats.RecordCacheDisabled(reason);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task InvalidateCacheAsync(string cacheName)
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_cacheName == cacheName)
            {
                _cacheName = null;
                _cacheKey = null;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>API hata gövdesinden kısa, okunabilir bir alıntı (error.message varsa o).</summary>
    private static string Excerpt(string responseBody)
    {
        string text = responseBody;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                text = msg.GetString() ?? responseBody;
        }
        catch (JsonException)
        {
        }

        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length > 240 ? text[..240] + "…" : text;
    }

    private static string? TryReadCacheName(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Hata gövdesi önbelleğin süresinin dolduğunu / bulunamadığını mı belirtiyor?</summary>
    public static bool IsCacheError(string responseBody) =>
        responseBody.Contains("cachedContent", StringComparison.OrdinalIgnoreCase) ||
        responseBody.Contains("cached content", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// usageMetadata alanından önbellek istatistiğini işler.
    /// promptTokenCount önbellekten okunan token'ları da içerir; önbelleksiz kısım = prompt - cached.
    /// </summary>
    public static void RecordCacheUsage(string responseBody, PromptCacheStats stats)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("usageMetadata", out var usage))
                return;

            long prompt = usage.TryGetProperty("promptTokenCount", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;
            long cached = usage.TryGetProperty("cachedContentTokenCount", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0;
            stats.Record(prompt - cached, cached, 0);
        }
        catch (JsonException)
        {
            // İstatistik yalnızca bilgi amaçlı.
        }
    }

    private static string BuildSystemInstruction(TranslationContext ctx)
    {
        return TranslationPromptHelper.BuildSystemPrompt(ctx,
            "Yanıtını SADECE ve SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
    }

    private static string BuildUserPrompt(IReadOnlyList<string> texts, TranslationContext ctx)
    {
        return TranslationPromptHelper.BuildUserJsonArrayPrompt(texts, ctx);
    }

    private static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];

                if (candidate.TryGetProperty("finishReason", out var finishReason))
                {
                    var reason = finishReason.GetString();
                    if (reason == "SAFETY")
                    {
                        throw new HttpRequestException("Gemini güvenlik filtresi (Safety Filter) bu metin grubunun çevrilmesini engelledi.");
                    }
                }

                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textProp))
                {
                    var rawText = textProp.GetString() ?? string.Empty;
                    return JsonTranslationParser.Parse(rawText, expectedCount);
                }
            }

            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }
}
