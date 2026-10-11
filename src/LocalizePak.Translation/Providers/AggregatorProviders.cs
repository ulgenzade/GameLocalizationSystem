using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation.Providers;

/// <summary>
/// Together AI API Entegrasyonu (api.together.xyz).
/// Llama 3.3, DeepSeek R1/V3, Qwen 2.5 72B ve açık kaynak modeller için yüksek hızlı sağlayıcı.
/// </summary>
public sealed class TogetherAiTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "meta-llama/Llama-3.3-70B-Instruct-Turbo";

    public string Name => "Together AI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public TogetherAiTranslationProvider(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public TogetherAiTranslationProvider(string apiKey, string model = "meta-llama/Llama-3.3-70B-Instruct-Turbo", HttpClient? httpClient = null)
        : this(httpClient)
    {
        Configure(apiKey, model);
    }

    public void Configure(string apiKey, string model = "meta-llama/Llama-3.3-70B-Instruct-Turbo")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "meta-llama/Llama-3.3-70B-Instruct-Turbo" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Together AI API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildSystemPrompt(context);
        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 8192,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Aşağıdaki metin dizisini çevir ve SADECE JSON dizisi olarak döndür:\n{arr.ToJsonString()}" }
            }
        };

        var effort = context.NormalizedReasoningEffort;
        if (effort is "low" or "medium" or "high")
        {
            requestBody["reasoning_effort"] = effort;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.together.xyz/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Together AI API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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

    private static string BuildSystemPrompt(TranslationContext ctx)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ctx.SystemPromptTemplate))
            sb.AppendLine(ctx.SystemPromptTemplate);
        else
        {
            sb.AppendLine($"Profesyonel oyun yerelleştirme uzmanısın. Metinleri {ctx.TargetLanguageName}'ye çevir.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X] etiketlerini ASLA değiştirme.");
        }
        sb.AppendLine("Yanıtını SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
        if (!string.IsNullOrWhiteSpace(ctx.UserDirective)) sb.AppendLine($"Direktif: {ctx.UserDirective}");
        return sb.ToString();
    }
}

/// <summary>
/// Mistral AI API Entegrasyonu (api.mistral.ai).
/// Mistral Large, Mistral Small, Codestral ve Ministral modelleri için resmi servis.
/// </summary>
public sealed class MistralTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "mistral-large-latest";

    public string Name => "Mistral AI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public MistralTranslationProvider(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public MistralTranslationProvider(string apiKey, string model = "mistral-large-latest", HttpClient? httpClient = null)
        : this(httpClient)
    {
        Configure(apiKey, model);
    }

    public void Configure(string apiKey, string model = "mistral-large-latest")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "mistral-large-latest" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Mistral AI API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildSystemPrompt(context);
        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 8192,
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Aşağıdaki metinleri çevir ve {{\"translations\": [...]}} JSON nesnesi olarak döndür:\n{arr.ToJsonString()}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Mistral AI API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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
                .GetString() ?? "{}";

            return JsonTranslationParser.Parse(content, expectedCount);
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }

    private static string BuildSystemPrompt(TranslationContext ctx)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ctx.SystemPromptTemplate))
            sb.AppendLine(ctx.SystemPromptTemplate);
        else
        {
            sb.AppendLine($"Profesyonel oyun yerelleştirme uzmanısın. İngilizce metinleri {ctx.TargetLanguageName}'ye çevir.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X] etiketlerini ASLA değiştirme.");
        }
        sb.AppendLine("Yanıtı {\"translations\": [\"çeviri 1\", \"çeviri 2\", ...]} formatında bir JSON nesnesi olarak döndür.");
        if (!string.IsNullOrWhiteSpace(ctx.UserDirective)) sb.AppendLine($"Direktif: {ctx.UserDirective}");
        return sb.ToString();
    }
}

/// <summary>
/// DeepInfra API Entegrasyonu (api.deepinfra.com).
/// Açık kaynak modeller için ekonomik ve yüksek verimli OpenAI uyumlu sunucu.
/// </summary>
public sealed class DeepInfraTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "meta-llama/Meta-Llama-3.1-70B-Instruct";

    public string Name => "DeepInfra";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public DeepInfraTranslationProvider(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public DeepInfraTranslationProvider(string apiKey, string model = "meta-llama/Meta-Llama-3.1-70B-Instruct", HttpClient? httpClient = null)
        : this(httpClient)
    {
        Configure(apiKey, model);
    }

    public void Configure(string apiKey, string model = "meta-llama/Meta-Llama-3.1-70B-Instruct")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "meta-llama/Meta-Llama-3.1-70B-Instruct" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("DeepInfra API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildSystemPrompt(context);
        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 8192,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Çevir ve JSON dizisi olarak döndür:\n{arr.ToJsonString()}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepinfra.com/v1/openai/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DeepInfra API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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

    private static string BuildSystemPrompt(TranslationContext ctx)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ctx.SystemPromptTemplate))
            sb.AppendLine(ctx.SystemPromptTemplate);
        else
        {
            sb.AppendLine($"Profesyonel oyun yerelleştirme uzmanısın. Metinleri {ctx.TargetLanguageName}'ye çevir.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X] etiketlerini ASLA değiştirme.");
        }
        sb.AppendLine("Yanıtını SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
        if (!string.IsNullOrWhiteSpace(ctx.UserDirective)) sb.AppendLine($"Direktif: {ctx.UserDirective}");
        return sb.ToString();
    }
}

/// <summary>
/// Perplexity AI API Entegrasyonu (api.perplexity.ai).
/// Sonar ve Sonar Pro modelleriyle gerçek zamanlı web doğrulama ve çeviri.
/// </summary>
public sealed class PerplexityTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "sonar-pro";

    public string Name => "Perplexity AI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public PerplexityTranslationProvider(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public PerplexityTranslationProvider(string apiKey, string model = "sonar-pro", HttpClient? httpClient = null)
        : this(httpClient)
    {
        Configure(apiKey, model);
    }

    public void Configure(string apiKey, string model = "sonar-pro")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "sonar-pro" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Perplexity AI API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildSystemPrompt(context);
        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 8192,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Çevir ve SADECE JSON dizisi olarak döndür:\n{arr.ToJsonString()}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.perplexity.ai/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Perplexity AI API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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

    private static string BuildSystemPrompt(TranslationContext ctx)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ctx.SystemPromptTemplate))
            sb.AppendLine(ctx.SystemPromptTemplate);
        else
        {
            sb.AppendLine($"Profesyonel oyun yerelleştirme uzmanısın. Metinleri {ctx.TargetLanguageName}'ye çevir.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X] etiketlerini ASLA değiştirme.");
        }
        sb.AppendLine("Yanıtını SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
        if (!string.IsNullOrWhiteSpace(ctx.UserDirective)) sb.AppendLine($"Direktif: {ctx.UserDirective}");
        return sb.ToString();
    }
}
