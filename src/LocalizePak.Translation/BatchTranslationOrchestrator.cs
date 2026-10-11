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

        // 1. ADIM: Çeviri Belleği (TM) AÇIKSA kontrol et
        if (EnableTranslationMemory)
        {
            foreach (var entry in validEntries)
            {
                if (_translationMemory.TryGet(entry.SourceText, context.TargetLanguage, out var cached) &&
                    !string.IsNullOrWhiteSpace(cached))
                {
                    entry.TranslatedText = cached;
                    entry.Status = TranslationStatus.Done;
                    entry.Notes = "⚡ Çeviri Belleği (TM) ile anında dolduruldu (0 Kota / 0 Maliyet)";
                    completed++;
                }
                else
                {
                    entriesForAi.Add(entry);
                }
            }

            if (completed > 0)
            {
                progress?.Report(new TranslationProgress(completed, total, errors,
                    $"⚡ {completed}/{total} satır Çeviri Belleğinden (TM) anında dolduruldu!"));
            }

            if (entriesForAi.Count == 0) return;
        }
        else
        {
            // TM KAPALI — Tüm satırlar doğrudan AI ile temiz çevrilecek
            entriesForAi = validEntries;
        }

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
                            provider, maskedTexts, context, maxRetries: 5, ct);
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
                            // AI boş döndü veya çeviri başarısız oldu — Sahte Done yapma, hata olarak bildir
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

    private static async Task<IReadOnlyList<string>> TranslateWithRetryAsync(
        ITranslationProvider provider,
        IReadOnlyList<string> texts,
        TranslationContext context,
        int maxRetries,
        CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await provider.TranslateBatchAsync(texts, context, ct);
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests &&
                attempt < maxRetries)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2, 4, 8, 16, 32 sn
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable &&
                attempt < maxRetries)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
        }
    }
}
