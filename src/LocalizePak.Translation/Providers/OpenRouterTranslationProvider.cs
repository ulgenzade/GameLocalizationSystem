using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation.Providers;

/// <summary>
/// OpenRouter (openrouter.ai) API Entegrasyonu.
/// Tek bir API anahtarıyla yüzlerce modele (Gemini, Claude, DeepSeek, Llama, OpenAI, Qwen) erişim sağlar.
/// </summary>
public sealed class OpenRouterTranslationProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private string _apiKey = string.Empty;
    private string _model = "deepseek/deepseek-chat";

    public string Name => "OpenRouter";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public OpenRouterTranslationProvider(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public OpenRouterTranslationProvider(string apiKey, string model = "deepseek/deepseek-chat", HttpClient? httpClient = null)
        : this(httpClient)
    {
        Configure(apiKey, model);
    }

    public void Configure(string apiKey, string model = "deepseek/deepseek-chat")
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "deepseek/deepseek-chat" : model;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("OpenRouter API anahtarı yapılandırılmamış.");

        var systemPrompt = Services.TranslationPromptHelper.BuildSystemPrompt(context,
            "Yanıt olarak SADECE ve SADECE JSON dizisi döndür ([\"çeviri 1\", \"çeviri 2\", ...]). Asla markdown veya açıklama yazma.");
        var userPrompt = Services.TranslationPromptHelper.BuildUserJsonArrayPrompt(maskedTexts, context);

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

        var effort = context.NormalizedReasoningEffort;
        if (effort is "low" or "medium" or "high")
        {
            requestBody["reasoning_effort"] = effort;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Add("HTTP-Referer", "https://github.com/LocalizePak/GameLocalizationSystem");
        request.Headers.Add("X-Title", "GameLocalizationSystem");
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenRouter API hatası ({response.StatusCode}): {responseBody}", null, response.StatusCode);

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
            sb.AppendLine($"Görevin: Oyun metinlerini {ctx.SourceLanguageName}'den {ctx.TargetLanguageName}'ye en doğal ve kaliteli şekilde çevirmek.");
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
            sb.AppendLine("TERİMLER SÖZLÜĞÜ (Bu çevirileri aynen uygula):");
            foreach (var g in ctx.Glossary)
            {
                if (!string.IsNullOrWhiteSpace(g.Translation))
                    sb.AppendLine($"- {g.Original} => {g.Translation}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("ÖNEMLİ KURAL: Yanıtın başı veya sonu hiçbir markdown (```json ... ```) veya açıklama içermemeli, doğrudan saf bir JSON dizisi [\"...\", \"...\"] olmalıdır.");

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
