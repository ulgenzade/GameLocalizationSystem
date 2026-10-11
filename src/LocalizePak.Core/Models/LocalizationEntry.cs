using LocalizePak.Core.Enums;

namespace LocalizePak.Core.Models;

/// <summary>
/// Tek bir çeviri girdisini temsil eder. Key-Value çifti artı durum bilgisi.
/// </summary>
public sealed class LocalizationEntry
{
    /// <summary>Oyun motorunun metni tanımlamak için kullandığı benzersiz anahtar.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Anahtarın binary formattaki hash değeri. Değiştirilmez!</summary>
    public uint KeyHash { get; set; }

    /// <summary>Orijinal oyun metni (kaynak dil).</summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>Çevrilmiş metin (hedef dil — Türkçe).</summary>
    public string TranslatedText { get; set; } = string.Empty;

    /// <summary>Bu girdinin mevcut çeviri durumu.</summary>
    public TranslationStatus Status { get; set; } = TranslationStatus.Pending;

    /// <summary>Çeviri sonrası değişken/etiket doğrulaması başarısız olduysa true.</summary>
    public bool HasVariableError { get; set; }

    /// <summary>Kullanıcı notu veya hata açıklaması.</summary>
    public string? Notes { get; set; }

    /// <summary>Bu girdinin ait olduğu namespace adı (görüntüleme amaçlı).</summary>
    public string NamespaceName { get; set; } = string.Empty;
}
