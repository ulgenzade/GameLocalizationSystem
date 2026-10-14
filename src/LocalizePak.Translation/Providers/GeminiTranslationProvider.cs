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
/// </summary>
public sealed class GeminiTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "gemini-3.5-flash-lite";

    public string Name => "Gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

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

        var genConfig = new JsonObject
        {
            ["temperature"] = 0.2,
            ["maxOutputTokens"] = 8192,
            ["responseMimeType"] = "application/json"
        };

        var effort = context.NormalizedReasoningEffort;
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
        else
        {
            genConfig["thinkingConfig"] = new JsonObject
            {
                ["thinkingBudget"] = 0
            };
        }

        var requestBody = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = systemInstruction }
                }
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = userPrompt }
                    }
                }
            },
            ["generationConfig"] = genConfig,
            ["safetySettings"] = new JsonArray
            {
                new JsonObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "BLOCK_NONE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_HATE_SPEECH", ["threshold"] = "BLOCK_NONE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "BLOCK_NONE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_DANGEROUS_CONTENT", ["threshold"] = "BLOCK_NONE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_CIVIC_INTEGRITY", ["threshold"] = "BLOCK_NONE" }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

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

                var fallbackUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{fallbackModel}:generateContent";
                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, fallbackUrl);
                fallbackRequest.Headers.Add("x-goog-api-key", _apiKey);
                fallbackRequest.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

                using var fallbackResponse = await _http.SendAsync(fallbackRequest, ct);
                var fallbackBody = await fallbackResponse.Content.ReadAsStringAsync(ct);

                if (fallbackResponse.IsSuccessStatusCode)
                {
                    _model = fallbackModel;
                    return ParseResponse(fallbackBody, maskedTexts.Count);
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || responseBody.Contains("RESOURCE_EXHAUSTED"))
            {
                throw new HttpRequestException(
                    "⚠️ Gemini API Kotası Doldu (429 - Günlük İstek Sınırı / Rate Limit). Lütfen Ayarlar'dan modeli değiştirin (örn. gemini-3.5-flash-lite) veya başka bir API anahtarı ekleyin.",
                    null,
                    System.Net.HttpStatusCode.TooManyRequests);
            }

            var safeErrorMessage = responseBody.Replace(_apiKey, "***");
            throw new HttpRequestException($"Gemini API hatası ({response.StatusCode}): {safeErrorMessage}", null, response.StatusCode);
        }

        return ParseResponse(responseBody, maskedTexts.Count);
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
