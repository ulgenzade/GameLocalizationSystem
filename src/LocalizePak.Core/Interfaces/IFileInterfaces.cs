using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.Core.Interfaces;

/// <summary>
/// .pak arşivi okuma işlemlerini soyutlayan arayüz.
/// </summary>
public interface IPakReader
{
    /// <summary>
    /// Belirtilen .pak dosyasını açar ve arşiv yapısını döndürür.
    /// </summary>
    /// <param name="filePath">Fiziksel dosya yolu.</param>
    /// <param name="aesKey">Şifrelenmiş arşivler için 256-bit Hex AES anahtarı. Opsiyonel.</param>
    /// <param name="ct">İptal token'ı.</param>
    Task<PakArchive> OpenAsync(string filePath, string? aesKey = null, CancellationToken ct = default);

    /// <summary>
    /// Arşiv içindeki belirtilen girdiyi bellek akışı olarak çıkarır.
    /// </summary>
    /// <param name="archive">Açılmış arşiv nesnesi.</param>
    /// <param name="entry">Çıkarılacak girdi.</param>
    /// <param name="ct">İptal token'ı.</param>
    Task<Stream> ExtractEntryAsync(PakArchive archive, PakEntry entry, CancellationToken ct = default);
}

/// <summary>
/// .pak arşivi yazma işlemlerini soyutlayan arayüz.
/// </summary>
public interface IPakWriter
{
    /// <summary>
    /// Verilen dosyaları yeni bir .pak arşivine yazar.
    /// </summary>
    /// <param name="outputPath">Çıktı dosyası yolu.</param>
    /// <param name="mountPoint">Sanal dosya sistemi mount noktası.</param>
    /// <param name="files">Arşive eklenecek dosyalar (sanal yol, veri çifti).</param>
    /// <param name="ct">İptal token'ı.</param>
    /// <param name="sourceFormat">Orijinal arşivin format türü. Doğru serileştiriciyi seçmek için kullanılır.</param>
    /// <param name="aesKey">Şifreli arşivler için 256-bit Hex AES anahtarı. Opsiyonel.</param>
    /// <param name="sourceArchivePath">Özgün arşivin yolu. Yerinde yamalanan formatlar (Unity) için gereklidir.</param>
    Task WriteAsync(
        string outputPath,
        string mountPoint,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        CancellationToken ct = default,
        PakFormat sourceFormat = PakFormat.Unknown,
        string? aesKey = null,
        string? sourceArchivePath = null);
}

/// <summary>
/// .locres binary formatı serileştirme/deserileştirme arayüzü.
/// </summary>
public interface ILocresSerializer
{
    /// <summary>
    /// Binary akışı okuyarak LocresFile nesnesine dönüştürür.
    /// </summary>
    Task<LocresFile> DeserializeAsync(Stream stream, CancellationToken ct = default);

    /// <summary>
    /// LocresFile nesnesini binary formatına serileştirir.
    /// </summary>
    Task<byte[]> SerializeAsync(LocresFile locresFile, CancellationToken ct = default);
}

/// <summary>
/// Çeviri ilerleme bildirimi için kullanılan veri yapısı.
/// </summary>
public record TranslationProgress(
    int CompletedEntries,
    int TotalEntries,
    int ErrorCount,
    string? CurrentBatchInfo = null)
{
    public double Percentage => TotalEntries > 0
        ? (double)(CompletedEntries + ErrorCount) / TotalEntries * 100.0
        : 0.0;
}
