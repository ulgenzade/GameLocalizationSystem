using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LocalizePak.Core.Models;
using LocalizePak.Translation.Providers;
using LocalizePak.Translation.Services;
using Xunit;

namespace LocalizePak.Tests;

/// <summary>
/// Claude prompt caching ve Gemini context caching davranışları.
/// Gerçek API çağrısı yapılmaz; istekler sahte bir HttpMessageHandler üzerinden yakalanır.
/// </summary>
public class PromptCachingTests
{
    // ─── Sahte HTTP altyapısı ────────────────────────────────────────────────

    private sealed record CapturedRequest(HttpMethod Method, string Url, string Body);

    private sealed class StubHandler(Func<CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            var captured = new CapturedRequest(request.Method, request.RequestUri!.ToString(), body);
            lock (Requests) Requests.Add(captured);
            return respond(captured);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static TranslationContext SmallContext() => new()
    {
        TargetLanguage = "tr",
        TargetLanguageName = "Türkçe"
    };

    private static TranslationContext LargeLoreContext() => new()
    {
        TargetLanguage = "tr",
        TargetLanguageName = "Türkçe",
        // ~20.000 karakter lore/direktif → tahmini ~5.000 token (explicit önbellek eşiğinin üstünde)
        UserDirective = string.Concat(Enumerable.Repeat("Eldoria krallığı karanlık bir fantezi dünyasıdır. ", 400))
    };

    private const string GeminiOkResponse =
        """{"candidates":[{"content":{"parts":[{"text":"[\"Merhaba\"]"}]}}],"usageMetadata":{"promptTokenCount":5000,"cachedContentTokenCount":4800}}""";

    // ─── Claude (Anthropic) ──────────────────────────────────────────────────

    [Fact]
    public void ClaudeBuildRequestBody_PutsCacheControlOnSystemBlock()
    {
        var body = ClaudeTranslationProvider.BuildRequestBody("claude-sonnet-5-5", "SYSTEM", "USER", "off");

        var system = body["system"]!.AsArray();
        Assert.Single(system);
        Assert.Equal("SYSTEM", system[0]!["text"]!.GetValue<string>());
        Assert.Equal("ephemeral", system[0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Equal("USER", body["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void ClaudeBuildRequestBody_ModernModel_UsesAdaptiveThinkingAndEffort()
    {
        var body = ClaudeTranslationProvider.BuildRequestBody("claude-sonnet-5-5", "S", "U", "medium");

        Assert.Equal("adaptive", body["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("medium", body["output_config"]!["effort"]!.GetValue<string>());
        Assert.Null(body["temperature"]);
        Assert.Null(body["thinking"]!["budget_tokens"]);
    }

    [Fact]
    public void ClaudeBuildRequestBody_ModernModel_EffortOff_SendsLowEffortWithoutThinkingParam()
    {
        var body = ClaudeTranslationProvider.BuildRequestBody("claude-opus-5-5", "S", "U", "off");

        Assert.Null(body["thinking"]);
        Assert.Null(body["temperature"]);
        Assert.Equal("low", body["output_config"]!["effort"]!.GetValue<string>());
    }

    [Fact]
    public void ClaudeBuildRequestBody_LegacyModel_KeepsBudgetTokensAndTemperature()
    {
        var body = ClaudeTranslationProvider.BuildRequestBody("claude-3-7-sonnet-20250219", "S", "U", "high");

        Assert.Equal("enabled", body["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal(4096, body["thinking"]!["budget_tokens"]!.GetValue<int>());
        Assert.Equal(1.0, body["temperature"]!.GetValue<double>());
        Assert.Null(body["output_config"]);
        Assert.NotNull(body["system"]![0]!["cache_control"]);
    }

    [Theory]
    [InlineData("claude-3-5-sonnet-20241022", true)]
    [InlineData("claude-3-7-sonnet-20250219", true)]
    [InlineData("claude-haiku-4-5", true)]
    [InlineData("claude-sonnet-4-20250514", true)]
    [InlineData("claude-sonnet-4-6", false)]
    [InlineData("claude-opus-4-8", false)]
    [InlineData("claude-sonnet-5-5", false)]
    [InlineData("claude-haiku-5-5", false)]
    [InlineData("claude-opus-5-5", false)]
    public void ClaudeIsLegacyModel_ClassifiesModelIds(string model, bool expected)
    {
        Assert.Equal(expected, ClaudeTranslationProvider.IsLegacyModel(model));
    }

    [Fact]
    public void ClaudeParseResponse_SkipsThinkingBlockAndReadsTextBlock()
    {
        const string response =
            """{"content":[{"type":"thinking","thinking":"","signature":"x"},{"type":"text","text":"[\"Merhaba\",\"Dünya\"]"}],"stop_reason":"end_turn"}""";

        var result = ClaudeTranslationProvider.ParseResponse(response, 2);

        Assert.Equal(["Merhaba", "Dünya"], result);
    }

    [Fact]
    public void ClaudeParseResponse_Refusal_Throws()
    {
        const string response = """{"content":[],"stop_reason":"refusal"}""";

        Assert.Throws<HttpRequestException>(() => ClaudeTranslationProvider.ParseResponse(response, 1));
    }

    [Fact]
    public async Task ClaudeTranslateBatchAsync_SendsCacheMarkerAndRecordsCacheUsage()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"content":[{"type":"text","text":"[\"Merhaba\"]"}],"stop_reason":"end_turn","usage":{"input_tokens":120,"cache_creation_input_tokens":0,"cache_read_input_tokens":900}}"""));
        var provider = new ClaudeTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "claude-sonnet-5-5");

        var result = await provider.TranslateBatchAsync(["Hello"], SmallContext());

        Assert.Equal(["Merhaba"], result);
        var sent = JsonNode.Parse(handler.Requests.Single().Body)!;
        Assert.Equal("ephemeral", sent["system"]![0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Equal(900, provider.CacheStats.CacheReadTokens);
        Assert.Equal(1020, provider.CacheStats.TotalInputTokens);
    }

    // ─── Gemini ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GeminiTranslate_SmallPrompt_DoesNotCreateExplicitCache()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, GeminiOkResponse));
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");

        await provider.TranslateBatchAsync(["Hello"], SmallContext());

        var request = Assert.Single(handler.Requests);
        Assert.Contains(":generateContent", request.Url);
        var body = JsonNode.Parse(request.Body)!;
        Assert.NotNull(body["system_instruction"]);
        Assert.Null(body["cachedContent"]);
    }

    [Fact]
    public async Task GeminiTranslate_LargePrompt_CreatesCacheOnceAndReusesIt()
    {
        var handler = new StubHandler(r => r.Url.EndsWith("/cachedContents")
            ? Json(HttpStatusCode.OK, """{"name":"cachedContents/abc123"}""")
            : Json(HttpStatusCode.OK, GeminiOkResponse));
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");
        var context = LargeLoreContext();

        await provider.TranslateBatchAsync(["Hello"], context);
        await provider.TranslateBatchAsync(["World"], context);

        var creates = handler.Requests.Where(r => r.Url.EndsWith("/cachedContents")).ToList();
        var create = Assert.Single(creates);
        var createBody = JsonNode.Parse(create.Body)!;
        Assert.Equal("models/gemini-3.5-flash-lite", createBody["model"]!.GetValue<string>());
        Assert.Equal("900s", createBody["ttl"]!.GetValue<string>());
        Assert.Contains("Eldoria", createBody["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());

        var generates = handler.Requests.Where(r => r.Url.Contains(":generateContent")).ToList();
        Assert.Equal(2, generates.Count);
        foreach (var g in generates)
        {
            var body = JsonNode.Parse(g.Body)!;
            Assert.Equal("cachedContents/abc123", body["cachedContent"]!.GetValue<string>());
            Assert.Null(body["system_instruction"]);
        }

        Assert.Equal(9600, provider.CacheStats.CacheReadTokens);
    }

    [Fact]
    public async Task GeminiTranslate_CacheCreationRejected_FallsBackInlineAndDoesNotRetry()
    {
        var handler = new StubHandler(r => r.Url.EndsWith("/cachedContents")
            ? Json(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Cached content is too small."}}""")
            : Json(HttpStatusCode.OK, GeminiOkResponse));
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");
        var context = LargeLoreContext();

        await provider.TranslateBatchAsync(["Hello"], context);
        await provider.TranslateBatchAsync(["World"], context);

        Assert.Single(handler.Requests, r => r.Url.EndsWith("/cachedContents"));
        foreach (var g in handler.Requests.Where(r => r.Url.Contains(":generateContent")))
        {
            var body = JsonNode.Parse(g.Body)!;
            Assert.NotNull(body["system_instruction"]);
            Assert.Null(body["cachedContent"]);
        }
        Assert.Contains("Cached content is too small", provider.CacheStats.CacheDisabledReason);
    }

    [Fact]
    public async Task GeminiTranslate_CacheCreationRateLimited_DisablesCacheForSession()
    {
        // Ücretsiz katman: explicit önbellek kotası 0 → 429. Her batch'te yeniden denemek istek kotasını tüketir.
        var handler = new StubHandler(r => r.Url.EndsWith("/cachedContents")
            ? Json(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"message":"Quota exceeded for metric: TotalCachedContentStorageTokensPerModelFreeTier, limit: 0","status":"RESOURCE_EXHAUSTED"}}""")
            : Json(HttpStatusCode.OK, GeminiOkResponse));
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");
        var context = LargeLoreContext();

        await provider.TranslateBatchAsync(["Hello"], context);
        await provider.TranslateBatchAsync(["World"], context);
        await provider.TranslateBatchAsync(["Again"], context);

        Assert.Single(handler.Requests, r => r.Url.EndsWith("/cachedContents"));
        Assert.Equal(3, handler.Requests.Count(r => r.Url.Contains(":generateContent")));
        Assert.Contains("FreeTier", provider.CacheStats.CacheDisabledReason);
    }

    [Fact]
    public async Task GeminiTranslate_CacheCreationServerError_RetriesOnNextBatch()
    {
        int creates = 0;
        var handler = new StubHandler(r => r.Url.EndsWith("/cachedContents")
            ? (++creates == 1
                ? Json(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"message":"overloaded"}}""")
                : Json(HttpStatusCode.OK, """{"name":"cachedContents/ok"}"""))
            : Json(HttpStatusCode.OK, GeminiOkResponse));
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");
        var context = LargeLoreContext();

        await provider.TranslateBatchAsync(["Hello"], context);
        await provider.TranslateBatchAsync(["World"], context);

        Assert.Equal(2, creates);
        var last = JsonNode.Parse(handler.Requests.Last(r => r.Url.Contains(":generateContent")).Body)!;
        Assert.Equal("cachedContents/ok", last["cachedContent"]!.GetValue<string>());
    }

    [Fact]
    public async Task GeminiTranslate_GenerateRateLimited_ErrorMessageIncludesApiDetail()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.TooManyRequests,
            """{"error":{"code":429,"message":"Quota exceeded for metric: generate_content_free_tier_requests, limit: 15","status":"RESOURCE_EXHAUSTED"}}"""));
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.TranslateBatchAsync(["Hello"], SmallContext()));

        Assert.Contains("generate_content_free_tier_requests", ex.Message);
        Assert.DoesNotContain("test-key", ex.Message);
    }

    [Fact]
    public async Task GeminiTranslate_ExpiredCacheOnServer_RetriesSameBatchInline()
    {
        int generateCalls = 0;
        var handler = new StubHandler(r =>
        {
            if (r.Url.EndsWith("/cachedContents"))
                return Json(HttpStatusCode.OK, """{"name":"cachedContents/expired"}""");

            generateCalls++;
            return generateCalls == 1
                ? Json(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"CachedContent not found (or permission denied)"}}""")
                : Json(HttpStatusCode.OK, GeminiOkResponse);
        });
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");

        var result = await provider.TranslateBatchAsync(["Hello"], LargeLoreContext());

        Assert.Equal(["Merhaba"], result);
        var generates = handler.Requests.Where(r => r.Url.Contains(":generateContent")).ToList();
        Assert.Equal(2, generates.Count);
        var retryBody = JsonNode.Parse(generates[1].Body)!;
        Assert.Null(retryBody["cachedContent"]);
        Assert.NotNull(retryBody["system_instruction"]);
    }

    [Fact]
    public async Task GeminiTranslate_CachedRequestInvalidArgument_RetriesInlineAndDisablesCache()
    {
        var handler = new StubHandler(r =>
        {
            if (r.Url.EndsWith("/cachedContents"))
                return Json(HttpStatusCode.OK, """{"name":"cachedContents/bad"}""");

            return r.Body.Contains("cachedContent")
                ? Json(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Request contains an invalid argument.","status":"INVALID_ARGUMENT"}}""")
                : Json(HttpStatusCode.OK, GeminiOkResponse);
        });
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-3.5-flash-lite");
        var context = LargeLoreContext();

        var first = await provider.TranslateBatchAsync(["Hello"], context);
        await provider.TranslateBatchAsync(["World"], context);

        Assert.Equal(["Merhaba"], first);
        Assert.Single(handler.Requests, r => r.Url.EndsWith("/cachedContents"));
        // 1. batch: önbellekli (400) + inline tekrar; 2. batch: doğrudan inline
        var generates = handler.Requests.Where(r => r.Url.Contains(":generateContent")).ToList();
        Assert.Equal(3, generates.Count);
        Assert.Null(JsonNode.Parse(generates[2].Body)!["cachedContent"]);
        Assert.Contains("invalid argument", provider.CacheStats.CacheDisabledReason);
    }

    [Fact]
    public async Task GeminiTranslate_CacheNearExpiry_CreatesNewCache()
    {
        int created = 0;
        var handler = new StubHandler(r => r.Url.EndsWith("/cachedContents")
            ? Json(HttpStatusCode.OK, $$"""{"name":"cachedContents/c{{++created}}"}""")
            : Json(HttpStatusCode.OK, GeminiOkResponse));
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var provider = new GeminiTranslationProvider(new HttpClient(handler)) { UtcNow = () => now };
        provider.Configure("test-key", "gemini-3.5-flash-lite");
        var context = LargeLoreContext();

        await provider.TranslateBatchAsync(["Hello"], context);
        now += GeminiTranslationProvider.CacheTtl - TimeSpan.FromSeconds(30); // yenileme payının içinde
        await provider.TranslateBatchAsync(["World"], context);

        Assert.Equal(2, created);
        var lastGenerate = JsonNode.Parse(handler.Requests.Last(r => r.Url.Contains(":generateContent")).Body)!;
        Assert.Equal("cachedContents/c2", lastGenerate["cachedContent"]!.GetValue<string>());
    }

    [Fact]
    public async Task GeminiTranslate_Gemini4Model_UsesImplicitCachingOnly()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, GeminiOkResponse));
        var provider = new GeminiTranslationProvider(new HttpClient(handler));
        provider.Configure("test-key", "gemini-4-argon");

        await provider.TranslateBatchAsync(["Hello"], LargeLoreContext());

        Assert.DoesNotContain(handler.Requests, r => r.Url.EndsWith("/cachedContents"));
    }

    [Fact]
    public void GeminiRecordCacheUsage_SplitsPromptIntoCachedAndUncached()
    {
        var stats = new PromptCacheStats();

        GeminiTranslationProvider.RecordCacheUsage(GeminiOkResponse, stats);

        Assert.Equal(4800, stats.CacheReadTokens);
        Assert.Equal(200, stats.UncachedInputTokens);
        Assert.Equal(5000, stats.TotalInputTokens);
    }

    // ─── Ortak istatistik ────────────────────────────────────────────────────

    [Fact]
    public void PromptCacheStats_SummaryEmptyWithoutReads_AndShowsRatioWithReads()
    {
        var stats = new PromptCacheStats();
        stats.Record(1000, 0, 2000);
        Assert.Equal(string.Empty, stats.ToSummary());

        stats.Record(100, 2000, 0);
        Assert.Equal(5100, stats.TotalInputTokens);
        Assert.Contains("%39", stats.ToSummary());
    }
}
