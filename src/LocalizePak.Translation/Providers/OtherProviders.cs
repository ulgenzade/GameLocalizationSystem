using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation.Providers;

/// <summary>
/// OpenAI Chat Completions API entegrasyonu (GPT-4o mini varsayılan).
/// </summary>
public sealed class OpenAiTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "gpt-6.1-sol";

    public string Name => "OpenAI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public OpenAiTranslationProvider(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public void Configure(string apiKey, string model = "gpt-6.1-sol")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-6.1-sol" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("OpenAI API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildSystemPrompt(context);
        var userPrompt = BuildUserPrompt(maskedTexts);

        var effort = context.NormalizedReasoningEffort;
        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 8192,
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
            }
        };

        if (effort is "low" or "medium" or "high")
        {
            requestBody["reasoning_effort"] = effort;
            if (_model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                _model.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                _model.Contains("astra", StringComparison.OrdinalIgnoreCase))
            {
                requestBody.Remove("temperature");
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

        return ParseResponse(responseBody, maskedTexts.Count);
    }

    private static string BuildSystemPrompt(TranslationContext ctx)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ctx.SystemPromptTemplate))
        {
            sb.AppendLine(ctx.SystemPromptTemplate);
        }
        else
        {
            sb.AppendLine($"Profesyonel oyun yerelleştirme uzmanısın. İngilizce metinleri {ctx.TargetLanguageName}'ye çevir.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X] etiketlerini ASLA değiştirme.");
        }
        sb.AppendLine("Yanıtı {\"translations\": [\"çeviri 1\", \"çeviri 2\", ...]} formatında bir JSON nesnesi olarak döndür.");

        if (!string.IsNullOrWhiteSpace(ctx.UserDirective))
            sb.AppendLine($"Özel Direktif: {ctx.UserDirective}");

        if (ctx.Glossary.Any())
        {
            sb.AppendLine("Özel isimler:");
            foreach (var e in ctx.Glossary) sb.AppendLine($"  {e}");
        }
        return sb.ToString();
    }

    private static string BuildUserPrompt(IReadOnlyList<string> texts)
    {
        var arr = new JsonArray(texts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        return $"Aşağıdaki metinleri aynı sırada çevir ve {{\"translations\": [...]}} JSON nesnesi olarak döndür:\n{arr.ToJsonString()}";
    }

    private static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            return JsonTranslationParser.Parse(content, expectedCount);
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }
}

/// <summary>
/// DeepSeek API entegrasyonu (OpenAI uyumlu endpoint).
/// </summary>
public sealed class DeepSeekTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "deepseek-chat";

    public string Name => "DeepSeek";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public DeepSeekTranslationProvider(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public void Configure(string apiKey, string model = "deepseek-chat")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "deepseek-chat" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("DeepSeek API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildPrompt(context);
        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 8192,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user",
                    ["content"] = $"Çevir (JSON dizisi olarak döndür):\n{arr.ToJsonString()}" }
            }
        };

        var deepSeekEffort = context.NormalizedReasoningEffort;
        if (deepSeekEffort is "low" or "medium" or "high")
        {
            requestBody["reasoning_effort"] = deepSeekEffort;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DeepSeek API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

        return ParseResponse(responseBody, maskedTexts.Count);
    }

    private static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "[]";

            return JsonTranslationParser.Parse(content, expectedCount);
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }

    private static string BuildPrompt(TranslationContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Oyun yerelleştirme uzmanısın. İngilizce metinleri {ctx.TargetLanguageName}'ye çevir.");
        sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X] etiketlerini değiştirme.");
        sb.AppendLine("Yanıtını SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
        if (!string.IsNullOrWhiteSpace(ctx.UserDirective)) sb.AppendLine(ctx.UserDirective);
        return sb.ToString();
    }
}
