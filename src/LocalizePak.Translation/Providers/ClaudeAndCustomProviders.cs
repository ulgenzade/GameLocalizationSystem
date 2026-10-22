using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.Translation.Services;

namespace LocalizePak.Translation.Providers;

/// <summary>
/// Anthropic Claude API entegrasyonu (Claude 5.5 ailesi ve eski Claude 3.x / 4.x modelleri).
/// Sistem istemi (kurallar + direktif/lore + sözlük) prompt caching ile önbelleğe alınır;
/// aynı çeviri oturumundaki sonraki batch'ler bu kısmı ~%90 indirimli okur.
/// </summary>
public sealed class ClaudeTranslationProvider : ITranslationProvider, IPromptCacheReporter
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "claude-sonnet-5-5";

    public string Name => "Claude (Anthropic)";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);
    public PromptCacheStats CacheStats { get; } = new();

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

        var systemPrompt = TranslationPromptHelper.BuildSystemPrompt(context,
            "MUTLAK ÇIKTI KURALI: Yanıtını SADECE ve SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]. Hiçbir markdown veya açıklama ekleme.");
        var userPrompt = TranslationPromptHelper.BuildUserJsonArrayPrompt(maskedTexts, context);

        var requestBody = BuildRequestBody(_model, systemPrompt, userPrompt, context.NormalizedReasoningEffort);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic Claude API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

        RecordCacheUsage(responseBody, CacheStats);
        return ParseResponse(responseBody, maskedTexts.Count);
    }

    /// <summary>
    /// Messages API istek gövdesini oluşturur.
    /// Sistem istemi tek bir text bloğu olarak gönderilir ve üzerine <c>cache_control</c> konur:
    /// istemin sabit kısmı (kurallar, direktif/lore, sözlük) önbelleğe yazılır, değişen kısım
    /// (her batch'in metinleri) kullanıcı mesajında, işaretçinin arkasında kalır.
    /// </summary>
    public static JsonObject BuildRequestBody(string model, string systemPrompt, string userPrompt, string effort)
    {
        var requestBody = new JsonObject
        {
            ["model"] = model,
            ["system"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = systemPrompt,
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                }
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userPrompt
                }
            }
        };

        bool thinkingRequested = effort is "low" or "medium" or "high";

        if (IsLegacyModel(model))
        {
            // Claude 3.x / 4.0–4.5: sabit düşünme bütçesi ve temperature hâlâ geçerli.
            if (thinkingRequested)
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
        }
        else
        {
            // Claude 4.6+ / 5.x: budget_tokens ve özel temperature değerleri 400 döndürür.
            // Düşünme derinliği yalnızca adaptive thinking + output_config.effort ile ayarlanır.
            requestBody["max_tokens"] = 16000;
            if (thinkingRequested)
                requestBody["thinking"] = new JsonObject { ["type"] = "adaptive" };
            requestBody["output_config"] = new JsonObject
            {
                ["effort"] = thinkingRequested ? effort : "low"
            };
        }

        return requestBody;
    }

    /// <summary>
    /// Claude 3.x, 2.x ve 4.0–4.5 modelleri eski istek şemasını (budget_tokens + temperature) kullanır.
    /// </summary>
    public static bool IsLegacyModel(string model)
    {
        var m = model.Trim().ToLowerInvariant();
        if (m.StartsWith("claude-3") || m.StartsWith("claude-2") || m.StartsWith("claude-instant"))
            return true;

        return System.Text.RegularExpressions.Regex.IsMatch(m, @"^claude-(opus|sonnet|haiku)-4(-[0-5])?(-\d{8})?$");
    }

    /// <summary>Yanıttaki usage alanından önbellek okuma/yazma token'larını istatistiğe işler.</summary>
    public static void RecordCacheUsage(string responseBody, PromptCacheStats stats)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("usage", out var usage))
                return;

            stats.Record(
                ReadLong(usage, "input_tokens"),
                ReadLong(usage, "cache_read_input_tokens"),
                ReadLong(usage, "cache_creation_input_tokens"));
        }
        catch (JsonException)
        {
            // İstatistik yalnızca bilgi amaçlı; bozuk usage çeviriyi durdurmamalı.
        }
    }

    private static long ReadLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

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

    /// <summary>
    /// Yanıttaki text bloklarını birleştirip JSON çeviri dizisini ayrıştırır.
    /// Düşünme açıkken content[0] bir "thinking" bloğu olabilir; bu yüzden ilk blok değil, text blokları okunur.
    /// </summary>
    public static IReadOnlyList<string> ParseResponse(string responseBody, int expectedCount)
    {
        string text;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("stop_reason", out var stopReason) && stopReason.GetString() == "refusal")
                throw new HttpRequestException("Claude güvenlik sınıflandırıcısı bu metin grubunun çevrilmesini reddetti (stop_reason: refusal).");

            var sb = new StringBuilder();
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                        block.TryGetProperty("text", out var t))
                    {
                        sb.Append(t.GetString());
                    }
                }
            }
            text = sb.Length > 0 ? sb.ToString() : responseBody;
        }
        catch (JsonException)
        {
            text = responseBody;
        }

        return JsonTranslationParser.Parse(text, expectedCount);
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
        var systemPrompt = TranslationPromptHelper.BuildSystemPrompt(context,
            "Yanıt olarak SADECE ve SADECE JSON dizisi döndür ([\"çeviri 1\", \"çeviri 2\", ...]). Asla markdown veya açıklama yazma.");
        var userPrompt = TranslationPromptHelper.BuildUserJsonArrayPrompt(maskedTexts, context);

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.1,
            ["max_tokens"] = 4096,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
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

        var systemPrompt = TranslationPromptHelper.BuildSystemPrompt(context,
            "Yanıtı SADECE ve SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]. Asla açıklama yazma.");
        var userPrompt = TranslationPromptHelper.BuildUserJsonArrayPrompt(maskedTexts, context);

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
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

        var systemPrompt = TranslationPromptHelper.BuildSystemPrompt(context,
            "Yanıtı SADECE ve SADECE JSON dizisi olarak döndür: [\"çeviri 1\", \"çeviri 2\", ...]. Asla açıklama yazma.");
        var userPrompt = TranslationPromptHelper.BuildUserJsonArrayPrompt(maskedTexts, context);

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.2,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
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

        var systemPrompt = TranslationPromptHelper.BuildSystemPrompt(context,
            "Yanıt olarak SADECE ve SADECE JSON dizisi döndür ([\"çeviri 1\", \"çeviri 2\", ...]). Asla markdown veya açıklama yazma.");
        var userPrompt = TranslationPromptHelper.BuildUserJsonArrayPrompt(maskedTexts, context);

        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.1,
            ["max_tokens"] = 4096,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
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
