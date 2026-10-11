using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalizePak.Translation.Services;

/// <summary>
/// Yerel AI model seçenekleri ve donanım gereksinimi bilgileri.
/// </summary>
public sealed class LocalAiModelOption
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
    public string SizeInfo { get; init; } = string.Empty;
    public string HardwareRequirement { get; init; } = string.Empty;
    public string RecommendationReason { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsRecommended { get; init; }

    public override string ToString() => $"{DisplayName} ({SizeInfo})";
}

/// <summary>
/// Model indirme ilerleme durumu.
/// </summary>
public sealed class OllamaPullProgress
{
    public string Status { get; set; } = string.Empty;
    public long CompletedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage { get; set; }
    public bool IsDone { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Yerel Ollama sunucusuyla iletişim kuran, model durumunu sorgulayan ve model indiren servis.
/// </summary>
public sealed class OllamaLocalModelService
{
    private readonly HttpClient _http;

    public OllamaLocalModelService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromHours(2) };
    }

    /// <summary>
    /// Yerel kullanım için 1'den 5'e sıralı (zayıftan güçlüye) 5 seçkin yerelleştirme modeli.
    /// </summary>
    public static IReadOnlyList<LocalAiModelOption> PresetModels { get; } =
    [
        new LocalAiModelOption
        {
            Id = "qwen2.5:3b",
            DisplayName = "1. Qwen 2.5 3B (Ultra Hafif & Hızlı)",
            Tier = "Seviye 1/5 • Giriş Seviyesi (CPU & 4 GB RAM)",
            SizeInfo = "~2.0 GB İndirme",
            HardwareRequirement = "4 GB+ Sistem RAM / Harici GPU gerekmez (Sadece CPU ile çalışır)",
            RecommendationReason = "Düşük Donanım / CPU Uyumu: Harici ekran kartı olmayan ofis bilgisayarlarında bile takılmadan çalışabilir.",
            Description = "En az kaynak tüketen modeldir. Düşük donanımlı dizüstü bilgisayarlarda bile akıcı çalışır. Hızlı menü, arayüz, eşya ve temel diyalog çevirileri için uygundur.",
            IsRecommended = false
        },
        new LocalAiModelOption
        {
            Id = "qwen2.5:7b",
            DisplayName = "2. Qwen 2.5 7B (En İdeal F/P & VRAM Dengesi • ⭐ Önerilen)",
            Tier = "Seviye 2/5 • Standart Oyun Bilgisayarı (4-6 GB VRAM)",
            SizeInfo = "~4.7 GB İndirme",
            HardwareRequirement = "8 GB+ RAM / 4-6 GB VRAM GPU (GTX 1660 / RTX 3050 / RTX 4050)",
            RecommendationReason = "⭐ Neden Önerilen? (VRAM & Çeviri Kalitesi Dengesi): Standart bir oyuncu bilgisayarının (4-6 GB VRAM) ekran kartı belleğine tamamen sığar, CPU'ya taşma yapmaz. Hem saniyedeki çeviri kelime hızı (hızlı batch akışı) hem de Türkçe gramer/doğallık başarımı açısından altın orandır.",
            Description = "Yerel çeviride altın standarttır. Olağanüstü Türkçe dil bilgisi, yüksek hız ve akıcı cümle yapıları sunar. GTX 1660 / RTX 3050 ve üzeri kartlarda tam GPU hızlandırmayla sorunsuz çalışır.",
            IsRecommended = true
        },
        new LocalAiModelOption
        {
            Id = "deepseek-r1:8b",
            DisplayName = "3. DeepSeek R1 8B (Akıl Yürütmeli & Derin Düşünme)",
            Tier = "Seviye 3/5 • Orta-Üst Donanım (6-8 GB VRAM)",
            SizeInfo = "~4.9 GB İndirme",
            HardwareRequirement = "12 GB+ RAM / 6-8 GB VRAM GPU (RTX 2060 / 3060 / 4060)",
            RecommendationReason = "Derin Muhakeme & Edebi Analiz Tercihi: Deyimleri, kelime oyunlarını ve felsefi oyun diyaloglarını önce düşünce zincirinde (chain-of-thought) analiz eder, ardından çevirir.",
            Description = "Akıl yürütme (reasoning) yetenekli model. Deyimleri, kelime oyunlarını ve karmaşık oyun diyaloglarını çevirmeden önce analiz eder.",
            IsRecommended = false
        },
        new LocalAiModelOption
        {
            Id = "qwen2.5:14b",
            DisplayName = "4. Qwen 2.5 14B (İleri Düzey RPG & Edebi Çeviri)",
            Tier = "Seviye 4/5 • Yüksek Donanım (8-12 GB VRAM)",
            SizeInfo = "~9.0 GB İndirme",
            HardwareRequirement = "16 GB+ RAM / 8-12 GB VRAM GPU (RTX 3060 12GB / RTX 4070)",
            RecommendationReason = "Ağır Hikayeli RPG Çeviri Kapasitesi: Baldur's Gate 3, Disco Elysium gibi zengin edebi metinlerde karakter ses tonunu ve tarihi üslubu korur.",
            Description = "Ağır RPG (Baldur's Gate 3, Divinity) ve zengin hikayeli oyunlar için profesyonel kalitede edebi üslup ve karakter ses tonu sunar.",
            IsRecommended = false
        },
        new LocalAiModelOption
        {
            Id = "qwen2.5:32b",
            DisplayName = "5. Qwen 2.5 32B (Amiral Gemisi • Maksimum Kalite)",
            Tier = "Seviye 5/5 • Güçlü İş İstasyonu (16 GB+ VRAM)",
            SizeInfo = "~19.0 GB İndirme",
            HardwareRequirement = "24 GB+ RAM / 16 GB+ VRAM GPU (RTX 3090 / RTX 4080 / RTX 4090)",
            RecommendationReason = "Maksimum Yerel Kapasite: Bulut LLM seviyesine en yakın yerel modeldir. Terminoloji sadakati ve argo adaptasyonu en üst düzeydedir.",
            Description = "İnsan çevirmen seviyesine en yakın yerel modeldir. Edebi derinlik, argo uyarlaması ve tutarlı terminoloji çevirisi sağlar.",
            IsRecommended = false
        }
    ];

    /// <summary>
    /// Ollama REST servisinin çalışır durumda olup olmadığını denetler.
    /// </summary>
    public async Task<bool> CheckOllamaRunningAsync(string endpointUrl = "http://localhost:11434", CancellationToken ct = default)
    {
        try
        {
            var baseUri = NormalizeBaseUrl(endpointUrl);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            using var res = await _http.GetAsync($"{baseUri}/api/version", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ollama üzerinde daha önce indirilmiş ve hazır modellerin listesini döndürür.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetInstalledModelsAsync(string endpointUrl = "http://localhost:11434", CancellationToken ct = default)
    {
        var list = new List<string>();
        try
        {
            var baseUri = NormalizeBaseUrl(endpointUrl);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));

            using var res = await _http.GetAsync($"{baseUri}/api/tags", cts.Token);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in modelsArr.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            list.Add(name);
                    }
                }
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Ollama REST API üzerinden modeli akış (stream) halinde indirir ve ilerlemeyi bildirir.
    /// </summary>
    public async Task PullModelAsync(
        string modelName,
        Action<OllamaPullProgress> onProgress,
        string endpointUrl = "http://localhost:11434",
        CancellationToken ct = default)
    {
        var baseUri = NormalizeBaseUrl(endpointUrl);
        var requestUrl = $"{baseUri}/api/pull";

        var payload = new JsonObject
        {
            ["name"] = modelName,
            ["stream"] = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errText = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Ollama model indirme hatası ({response.StatusCode}): {errText}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var status = root.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? string.Empty : string.Empty;
                long completed = root.TryGetProperty("completed", out var cProp) && cProp.TryGetInt64(out var cVal) ? cVal : 0;
                long total = root.TryGetProperty("total", out var tProp) && tProp.TryGetInt64(out var tVal) ? tVal : 0;

                double pct = 0;
                if (total > 0)
                {
                    pct = Math.Round((double)completed / total * 100.0, 1);
                }

                bool isDone = status.Equals("success", StringComparison.OrdinalIgnoreCase);

                onProgress(new OllamaPullProgress
                {
                    Status = status,
                    CompletedBytes = completed,
                    TotalBytes = total,
                    Percentage = pct,
                    IsDone = isDone
                });

                if (isDone) break;
            }
            catch { }
        }
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "http://localhost:11434";
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].TrimEnd('/');
        return trimmed;
    }
}
