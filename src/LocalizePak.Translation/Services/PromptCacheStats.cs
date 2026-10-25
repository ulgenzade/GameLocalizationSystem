namespace LocalizePak.Translation.Services;

/// <summary>
/// Bağlamsal önbellekleme (prompt / context caching) destekleyen sağlayıcıların ortak arayüzü.
/// UI, çeviri bitiminde önbellekten okunan token oranını bu arayüz üzerinden raporlar.
/// </summary>
public interface IPromptCacheReporter
{
    PromptCacheStats CacheStats { get; }
}

/// <summary>
/// Bir çeviri oturumu boyunca biriken önbellek kullanım istatistikleri (thread-safe).
/// Paralel batch işçileri aynı sağlayıcı örneğini paylaştığı için sayaçlar Interlocked ile güncellenir.
/// </summary>
public sealed class PromptCacheStats
{
    private long _requests;
    private long _uncachedInputTokens;
    private long _cacheReadTokens;
    private long _cacheWriteTokens;

    public long Requests => Interlocked.Read(ref _requests);
    public long UncachedInputTokens => Interlocked.Read(ref _uncachedInputTokens);
    public long CacheReadTokens => Interlocked.Read(ref _cacheReadTokens);
    public long CacheWriteTokens => Interlocked.Read(ref _cacheWriteTokens);

    /// <summary>Toplam istem (prompt) token'ı = önbelleksiz + önbelleğe yazılan + önbellekten okunan.</summary>
    public long TotalInputTokens => UncachedInputTokens + CacheReadTokens + CacheWriteTokens;

    /// <summary>İstem token'larının önbellekten okunan oranı (0.0 – 1.0).</summary>
    public double CacheHitRatio => TotalInputTokens == 0 ? 0 : CacheReadTokens / (double)TotalInputTokens;

    /// <summary>Önbellek oluşturulamadıysa nedeni (kullanıcıya gösterilir); yoksa null.</summary>
    public string? CacheDisabledReason { get; private set; }

    public void Record(long uncachedInputTokens, long cacheReadTokens, long cacheWriteTokens)
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Add(ref _uncachedInputTokens, Math.Max(0, uncachedInputTokens));
        Interlocked.Add(ref _cacheReadTokens, Math.Max(0, cacheReadTokens));
        Interlocked.Add(ref _cacheWriteTokens, Math.Max(0, cacheWriteTokens));
    }

    public void RecordCacheDisabled(string reason) => CacheDisabledReason = reason;

    /// <summary>
    /// Durum çubuğu için kısa özet. Önbellek okuması varsa oranı, önbellek devre dışı kaldıysa nedenini,
    /// ikisi de yoksa boş metin döner.
    /// </summary>
    public string ToSummary()
    {
        if (CacheReadTokens > 0)
            return $"💾 Önbellek: {CacheReadTokens:N0} / {TotalInputTokens:N0} istem token'ı önbellekten okundu (%{CacheHitRatio * 100:F0}).";

        if (CacheDisabledReason != null)
            return $"💾 Önbellek kullanılamadı: {CacheDisabledReason}";

        return string.Empty;
    }
}
