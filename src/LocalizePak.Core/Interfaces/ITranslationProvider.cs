using LocalizePak.Core.Models;

namespace LocalizePak.Core.Interfaces;

/// <summary>
/// AI çeviri servislerinin uygulaması gereken sözleşme.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>Servis adı (görüntüleme ve seçim amaçlı).</summary>
    string Name { get; }

    /// <summary>Bu sağlayıcının yapılandırılmış ve kullanıma hazır olup olmadığı.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Verilen metin listesini hedef dile toplu olarak çevirir.
    /// Giriş listesindeki sıra çıkış listesinde korunmalıdır.
    /// </summary>
    /// <param name="maskedTexts">Değişkenleri ve özel isimleri maskelenmiş metin listesi.</param>
    /// <param name="context">Çeviri bağlamı (direktif, glosary, hedef dil).</param>
    /// <param name="ct">İptal token'ı.</param>
    /// <returns>Çevrilmiş metin listesi (maskedTexts ile aynı sırada).</returns>
    Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> maskedTexts,
        TranslationContext context,
        CancellationToken ct = default);
}
