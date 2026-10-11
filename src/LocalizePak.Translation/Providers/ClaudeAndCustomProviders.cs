using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation.Providers;

/// <summary>
/// Anthropic Claude API entegrasyonu (Claude 3.5 Sonnet, Claude 3.7 Sonnet, Claude 3.5 Haiku).
/// </summary>
public sealed class ClaudeTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "claude-sonnet-5-5";

    public string Name => "Claude (Anthropic)";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public ClaudeTranslationProvider(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public void Configure(string apiKey, string model = "claude-sonnet-5-5")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "claude-sonnet-5-5" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Anthropic Claude API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildSystemPrompt(context);
        var userPrompt = BuildUserPrompt(maskedTexts);

        var effort = context.NormalizedReasoningEffort;
        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userPrompt
                }
            }
        };

        if (effort is "low" or "medium" or "high")
        {
            int budgetTokens = effort switch
            {
                "low" => 1024,
                "medium" => 2048,
                "high" => 4096,
                _ => 1024
            };
            requestBody["max_tokens"] = 8192 + budgetTokens;
            requestBody["temperature"] = 1.0; // Anthropic spec: thinking requires temperature 1.0
            requestBody["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = budgetTokens
            };
        }
        else
        {
            requestBody["max_tokens"] = 4096;
            requestBody["temperature"] = 0.2;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic Claude API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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
            sb.AppendLine($"Sen dünyanın en iyi profesyonel video oyunu yerelleştirme uzmanısın.");
            sb.AppendLine($"Görevin: Oyun metinlerini İngilizce'den {ctx.TargetLanguageName}'ye en doğal ve kaliteli şekilde çevirmek.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [ETAG_X], [TMPTAG_X], [NAME_X] etiketlerini ASLA değiştirme veya silme.");
        }

        sb.AppendLine();
        sb.AppendLine("MUTLAK ÇIKTI KURALI:");
        sb.AppendLine("Yanıtını SADECE ve SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
        sb.AppendLine("Hiçbir markdown kod bloğu (` ```json `), sohbet veya açıklama metni ekleme.");

        if (!string.IsNullOrWhiteSpace(ctx.UserDirective))
            sb.AppendLine($"\nÖzel Direktif:\n{ctx.UserDirective}");

        if (ctx.Glossary.Any())
        {
            sb.AppendLine("\nÖzel İsimler & Terimler:");
            foreach (var e in ctx.Glossary) sb.AppendLine($"  - {e}");
        }

        return sb.ToString();
    }

    private static string BuildUserPrompt(IReadOnlyList<string> texts)
    {
        var arr = new JsonArray(texts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        return $"Aşağıdaki metinleri aynı sırada çevir ve sadece JSON dizisi olarak döndür:\n{arr.ToJsonString()}";
    }

    private static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "[]";

            return JsonTranslationParser.Parse(content, expectedCount);
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }
}

/// <summary>
/// Özel Uç Nokta / Yerel LLM sağlayıcısı (Ollama, LM Studio, OpenRouter, Groq, Mistral).
/// OpenAI uyumlu /v1/chat/completions endpointlerini destekler.
/// </summary>
public sealed class CustomEndpointTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _endpointUrl = "http://localhost:11434/v1/chat/completions";
    private string _apiKey = string.Empty;
    private string _model = "llama3";

    public string Name => "Özel API / Yerel LLM";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpointUrl);

    public CustomEndpointTranslationProvider(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public void Configure(string endpointUrl, string apiKey, string model)
    {
        _endpointUrl = string.IsNullOrWhiteSpace(endpointUrl)
            ? "http://localhost:11434/v1/chat/completions"
            : endpointUrl.TrimEnd('/');

        if (!_endpointUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            _endpointUrl = _endpointUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? _endpointUrl + "/chat/completions"
                : _endpointUrl + "/v1/chat/completions";
        }

        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "llama3" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(context);
        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.1,
            ["max_tokens"] = 4096,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Aşağıdaki {maskedTexts.Count} metni aynı sırada çevir. Yanıt olarak SADECE ve SADECE JSON dizisi döndür ([\"çeviri 1\", \"çeviri 2\", ...]):\n{arr.ToJsonString()}" }
            }
        };

        var customEffort = context.NormalizedReasoningEffort;
        if (customEffort is "low" or "medium" or "high")
        {
            requestBody["reasoning_effort"] = customEffort;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Özel LLM API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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
            sb.AppendLine("Sen dünyanın en iyi profesyonel video oyunu yerelleştirme uzmanısın.");
            sb.AppendLine($"Görevin: Oyun metinlerini İngilizce'den {ctx.TargetLanguageName}'ye en doğal ve kaliteli şekilde çevirmek.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [ETAG_X], [TMPTAG_X], [NAME_X], [GTAG_X] etiketlerini ASLA değiştirme veya silme.");
        }

        if (!string.IsNullOrWhiteSpace(ctx.UserDirective))
        {
            sb.AppendLine();
            sb.AppendLine($"ÖZEL DİREKTİF:\n{ctx.UserDirective}");
        }

        if (ctx.Glossary.Any())
        {
            sb.AppendLine();
            sb.AppendLine("ÖZEL İSİMLER VE TERİMLER (GLOSSARY):");
            foreach (var entry in ctx.Glossary)
                sb.AppendLine($"  - {entry}");
        }

        sb.AppendLine();
        sb.AppendLine("MUTLAK ÇIKTI KURALI:");
        sb.AppendLine("Yanıtını SADECE ve SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
        sb.AppendLine("Asla açıklama, markdown veya sohbet ekleme.");

        return sb.ToString();
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
}

/// <summary>
/// Kimi / Moonshot AI Çeviri Sağlayıcısı (moonshot-v1-8k, moonshot-v1-32k, moonshot-v1-128k).
/// </summary>
public sealed class KimiTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "moonshot-v1-8k";

    public string Name => "Kimi (Moonshot)";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public KimiTranslationProvider(HttpClient httpClient) => _http = httpClient;

    public void Configure(string apiKey, string model = "moonshot-v1-8k")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "moonshot-v1-8k" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Kimi (Moonshot) API anahtarı yapılandırılmamış.");

        var systemPrompt = context.SystemPromptTemplate;
        if (string.IsNullOrWhiteSpace(systemPrompt))
            systemPrompt = $"Oyun yerelleştirme uzmanısın. Metinleri {context.TargetLanguageName}'ye çevir. [VAR_X], [TAG_X] etiketlerini koru. Yanıtı SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\"]";

        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Aşağıdaki JSON dizisindeki metinleri çevir ve sadece JSON dizisi olarak döndür:\n{arr.ToJsonString()}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.moonshot.cn/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Kimi API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

        return ParseResponse(responseBody, maskedTexts.Count);
    }

    private static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";
            return JsonTranslationParser.Parse(content, expectedCount);
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }
}

/// <summary>
/// Qwen / Alibaba Cloud DashScope Çeviri Sağlayıcısı (qwen-turbo, qwen-plus, qwen-max).
/// </summary>
public sealed class QwenTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "qwen-plus";

    public string Name => "Qwen (Alibaba)";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public QwenTranslationProvider(HttpClient httpClient) => _http = httpClient;

    public void Configure(string apiKey, string model = "qwen-plus")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "qwen-plus" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Qwen (DashScope) API anahtarı yapılandırılmamış.");

        var systemPrompt = context.SystemPromptTemplate;
        if (string.IsNullOrWhiteSpace(systemPrompt))
            systemPrompt = $"Oyun yerelleştirme uzmanısın. Metinleri {context.TargetLanguageName}'ye çevir. [VAR_X], [TAG_X] etiketlerini koru. Yanıtı SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\"]";

        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Aşağıdaki JSON dizisindeki metinleri çevir ve sadece JSON dizisi olarak döndür:\n{arr.ToJsonString()}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Qwen API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

        return ParseResponse(responseBody, maskedTexts.Count);
    }

    private static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";
            return JsonTranslationParser.Parse(content, expectedCount);
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }
}

/// <summary>
/// Groq Cloud API Çeviri Sağlayıcısı (Llama 3.3 70B, Qwen 2.5 32B, Llama 3.1 8B, DeepSeek R1).
/// Dünyanın en hızlı LPU çıkarım altyapısını kullanır.
/// </summary>
public sealed class GroqTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "openai/gpt-oss-20b";

    public string Name => "Groq (Ücretsiz & Süper Hızlı)";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public GroqTranslationProvider(HttpClient httpClient) => _http = httpClient;

    public void Configure(string apiKey, string model = "openai/gpt-oss-20b")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "openai/gpt-oss-20b" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Groq API anahtarı yapılandırılmamış.");

        var systemPrompt = BuildSystemPrompt(context);
        var arr = new JsonArray(maskedTexts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.1,
            ["max_tokens"] = 4096,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = $"Aşağıdaki {maskedTexts.Count} metni aynı sırada çevir. Yanıt olarak SADECE ve SADECE JSON dizisi döndür ([\"çeviri 1\", \"çeviri 2\", ...]):\n{arr.ToJsonString()}" }
            }
        };

        var groqEffort = context.NormalizedReasoningEffort;
        if (groqEffort is "low" or "medium" or "high")
        {
            requestBody["reasoning_effort"] = groqEffort;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Groq API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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
            sb.AppendLine("Sen dünyanın en iyi profesyonel video oyunu yerelleştirme uzmanısın.");
            sb.AppendLine($"Görevin: Oyun metinlerini İngilizce'den {ctx.TargetLanguageName}'ye en doğal ve kaliteli şekilde çevirmek.");
            sb.AppendLine("[VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [ETAG_X], [TMPTAG_X], [NAME_X], [GTAG_X] etiketlerini ASLA değiştirme veya silme.");
        }

        if (!string.IsNullOrWhiteSpace(ctx.UserDirective))
        {
            sb.AppendLine();
            sb.AppendLine($"ÖZEL DİREKTİF:\n{ctx.UserDirective}");
        }

        if (ctx.Glossary.Any())
        {
            sb.AppendLine();
            sb.AppendLine("ÖZEL İSİMLER VE TERİMLER (GLOSSARY):");
            foreach (var entry in ctx.Glossary)
                sb.AppendLine($"  - {entry}");
        }

        sb.AppendLine();
        sb.AppendLine("MUTLAK ÇIKTI KURALI:");
        sb.AppendLine("Yanıtını SADECE ve SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]");
        sb.AppendLine("Asla açıklama, markdown veya sohbet ekleme.");

        return sb.ToString();
    }

    private static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";
            return JsonTranslationParser.Parse(content, expectedCount);
        }
        catch
        {
            return JsonTranslationParser.Parse(responseBody, expectedCount);
        }
    }
}
