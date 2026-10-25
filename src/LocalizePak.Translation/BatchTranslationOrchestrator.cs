using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation;

/// <summary>
/// Toplu çeviri orkestratörü.
/// Metinleri batch'lere böler, eşzamanlı API istekleri gönderir,
/// Rate-Limit durumunda Exponential Backoff ile yeniden dener.
/// </summary>
public sealed class BatchTranslationOrchestrator
{
    private readonly MaskingEngine _masking = new();
    private readonly TranslationMemory _translationMemory = new();

    /// <summary>
    /// Çeviri Belleği (TM) açık mı? (Kapalıyken her satır sıfırdan ve temiz şekilde AI ile çevrilir).
    /// </summary>
    public bool EnableTranslationMemory { get; set; } = false;

    public TranslationMemory TranslationMemory => _translationMemory;

    /// <summary>
    /// LocresFile içindeki tüm Pending girdileri toplu olarak çevirir.
    /// </summary>
    public async Task TranslateAllAsync(
        LocresFile locresFile,
        ITranslationProvider provider,
        TranslationContext context,
        int batchSize = 25,
        int maxConcurrency = 5,
        IProgress<TranslationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var pending = locresFile.AllEntries
            .Where(e => e.Status == TranslationStatus.Pending && !string.IsNullOrWhiteSpace(e.SourceText))
            .ToList();

        await TranslateEntriesAsync(pending, provider, context, batchSize, maxConcurrency, progress, ct);
    }

    /// <summary>
    /// Belirtilen girdi listesini toplu olarak çevirir.
    /// </summary>
    public async Task TranslateEntriesAsync(
        IReadOnlyList<LocalizationEntry> entriesToTranslate,
        ITranslationProvider provider,
        TranslationContext context,
        int batchSize = 25,
        int maxConcurrency = 5,
        IProgress<TranslationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var validEntries = entriesToTranslate
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceText))
            .ToList();

        int total = validEntries.Count;
        if (total == 0) return;

        int completed = 0;
        int errors = 0;

        var entriesForAi = new List<LocalizationEntry>();

        // 1. ADIM: Sembol/Noktalama Kontrolü ve Çeviri Belleği (TM)
        foreach (var entry in validEntries)
        {
            // Eğer metin sadece semboller, noktalama veya rakamlardan ibaretse (örn: "++", "--", "-", "----------------")
            // Yapay zekaya gönderip kotayı yakmaya gerek yok, doğrudan orijinali korunur.
            if (IsSymbolOrPunctuationOnly(entry.SourceText))
            {
                entry.TranslatedText = entry.SourceText;
                entry.Status = TranslationStatus.Done;
                entry.Notes = "⚡ Sembol/sayı filtresi: Doğrudan aktarıldı (0 kota)";
                completed++;
                continue;
            }

            if (EnableTranslationMemory &&
                _translationMemory.TryGet(entry.SourceText, context.TargetLanguage, out var cached) &&
                !string.IsNullOrWhiteSpace(cached))
            {
                entry.TranslatedText = cached;
                entry.Status = TranslationStatus.Done;
                entry.Notes = "⚡ Çeviri Belleği (TM) ile anında dolduruldu (0 Kota / 0 Maliyet)";
                completed++;
                continue;
            }

            entriesForAi.Add(entry);
        }

        if (completed > 0)
        {
            progress?.Report(new TranslationProgress(completed, total, errors,
                $"⚡ {completed}/{total} satır hazır olarak tamamlandı (Sembol filtresi / TM)!"));
        }

        if (entriesForAi.Count == 0) return;

        // Satırları Batch'lere Böl (Token kesintisini önlemek için optimize edildi)
        var batches = entriesForAi
            .Chunk(batchSize)
            .ToList();

        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        List<Task>? taskList = null;
        try
        {
            taskList = batches.Select(async (batch, batchIdx) =>
            {
                try
                {
                    await semaphore.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    foreach (var e in batch)
                    {
                        if (e.Status == TranslationStatus.Translating)
                            e.Status = TranslationStatus.Pending;
                    }
                    throw;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    ct.ThrowIfCancellationRequested();

                    // Küresel Rate-Limit soğuma kontrolü
                    if (DateTime.UtcNow < _rateLimitCooldownUntil)
                    {
                        var wait = _rateLimitCooldownUntil - DateTime.UtcNow;
                        if (wait > TimeSpan.Zero)
                        {
                            progress?.Report(new TranslationProgress(completed, total, errors,
                                $"⏳ API Hız Sınırı (Rate Limit) devrede. Kota yenilenmesi için {Math.Ceiling(wait.TotalSeconds)} sn bekleniyor..."));
                            await Task.Delay(wait, ct);
                        }
                    }

                    // Batch'teki girdileri işaretle
                    foreach (var e in batch) e.Status = TranslationStatus.Translating;

                    // Maskeleme
                    var masked = batch.Select(e =>
                        _masking.Mask(e.SourceText, context.Glossary)).ToList();
                    var maskedTexts = masked.Select(m => m.MaskedText).ToList();

                    // API çağrısı (Exponential Backoff ile)
                    IReadOnlyList<string> translations;
                    try
                    {
                        translations = await TranslateWithRetryAsync(
                            provider, maskedTexts, context, maxRetries: 4, progress, completed, total, errors, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        foreach (var e in batch)
                        {
                            if (e.Status == TranslationStatus.Translating)
                                e.Status = TranslationStatus.Pending;
                        }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Batch başarısız — girdileri Error olarak işaretle
                        foreach (var e in batch)
                        {
                            e.Status = TranslationStatus.Error;
                            e.Notes = ex.Message;
                        }
                        Interlocked.Add(ref errors, batch.Length);
                        progress?.Report(new TranslationProgress(completed, total, errors,
                            $"Batch {batchIdx + 1}/{batches.Count} başarısız ({ex.Message})"));
                        return;
                    }

                    // Sonuçları girdilere uygula
                    int batchSuccessCount = 0;
                    int batchErrorCount = 0;
                    for (int i = 0; i < batch.Length; i++)
                    {
                        var entry = batch[i];
                        var translated = i < translations.Count ? translations[i] : string.Empty;

                        // Maskeleri geri yükle ve eksik etiketleri otomatik onar
                        var restored = _masking.RestoreAndRepair(translated, masked[i].RestoreMap);

                        if (!string.IsNullOrWhiteSpace(restored))
                        {
                            entry.TranslatedText = restored;
                            entry.Status = TranslationStatus.Done;
                            entry.HasVariableError = false;
                            entry.Notes = null;
                            batchSuccessCount++;

                            if (EnableTranslationMemory)
                            {
                                _translationMemory.Add(entry.SourceText, context.TargetLanguage, restored);
                            }
                        }
                        else
                        {
                            // AI boş döndü veya çeviri başarısız oldu — Sahte Done yapma, hata olarak bildir.
                            // Satırda önceden (ör. elle düzeltilmiş) bir çeviri varsa onu silme; yoksa kaynak metni koy.
                            if (string.IsNullOrWhiteSpace(entry.TranslatedText))
                                entry.TranslatedText = entry.SourceText;
                            entry.Status = TranslationStatus.Error;
                            entry.Notes = "AI çevirisi boş döndü veya parse edilemedi.";
                            batchErrorCount++;
                        }
                    }

                    Interlocked.Add(ref completed, batchSuccessCount);
                    if (batchErrorCount > 0)
                    {
                        Interlocked.Add(ref errors, batchErrorCount);
                    }

                    progress?.Report(new TranslationProgress(completed, total, errors,
                        $"Batch {batchIdx + 1}/{batches.Count} tamamlandı ({batchSuccessCount} başarılı, {batchErrorCount} hata)"));
                }
                finally
                {
                    try
                    {
                        semaphore.Release();
                    }
                    catch (ObjectDisposedException) { }
                }
            }).ToList();

            await Task.WhenAll(taskList);
        }
        catch (OperationCanceledException)
        {
            // İptal edildiğinde Translating durumunda kalan satırları Pending yap
            foreach (var e in entriesForAi)
            {
                if (e.Status == TranslationStatus.Translating)
                    e.Status = TranslationStatus.Pending;
            }
            throw;
        }
        finally
        {
            // Tüm arka plan task'larının sonlanmasını bekle, ardından güvenle dispose et
            if (taskList != null)
            {
                try { await Task.WhenAll(taskList.Select(t => t.ContinueWith(_ => { }))); } catch { }
            }
            semaphore.Dispose();
        }

        if (EnableTranslationMemory)
        {
            await _translationMemory.SaveAsync();
        }
    }

    private static readonly SemaphoreSlim _rateLimitLock = new(1, 1);
    private static DateTime _rateLimitCooldownUntil = DateTime.MinValue;

    /// <summary>
    /// Metnin sadece noktalama işaretleri, ayraçlar, tireler veya rakamlardan ibaret olup olmadığını belirler.
    /// (örn: "++", "--", "-", "------------------------------------", "123", "///").
    /// </summary>
    private static bool IsSymbolOrPunctuationOnly(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        // İçerisinde en az 1 harf (Letter) yoksa (sadece sembol, noktalama, sayı, boşluk)
        return !text.Any(char.IsLetter);
    }

    private static async Task<IReadOnlyList<string>> TranslateWithRetryAsync(
        ITranslationProvider provider,
        IReadOnlyList<string> texts,
        TranslationContext context,
        int maxRetries,
        IProgress<TranslationProgress>? progress,
        int completed,
        int total,
        int errors,
        CancellationToken ct)
    {
        int attempt = 0;
        int effectiveMaxRetries = Math.Max(3, maxRetries);
        while (true)
        {
            try
            {
                return await provider.TranslateBatchAsync(texts, context, ct);
            }
            catch (HttpRequestException ex) when (
                (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                 ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                 ex.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("Rate Limit", StringComparison.OrdinalIgnoreCase)) &&
                attempt < effectiveMaxRetries)
            {
                attempt++;
                // 429 durumunda küresel soğuma süresini güncelle (8-20 sn)
                var cooldown = TimeSpan.FromSeconds(Math.Min(25, 8 + (attempt * 4)));
                await _rateLimitLock.WaitAsync(ct);
                try
                {
                    _rateLimitCooldownUntil = DateTime.UtcNow.Add(cooldown);
                }
                finally
                {
                    _rateLimitLock.Release();
                }

                progress?.Report(new TranslationProgress(completed, total, errors,
                    $"⏳ API Hız Sınırı (Rate Limit: 429) aşıldı. Kota yenilenmesi için {cooldown.TotalSeconds:F0} sn bekleniyor... (Deneme {attempt}/{effectiveMaxRetries})"));

                await Task.Delay(cooldown, ct);
            }
        }
    }
}

