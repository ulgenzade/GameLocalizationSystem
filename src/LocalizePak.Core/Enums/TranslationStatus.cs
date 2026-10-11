namespace LocalizePak.Core.Enums;

/// <summary>
/// Tek bir çeviri girdisinin mevcut durumunu temsil eder.
/// </summary>
public enum TranslationStatus
{
    /// <summary>Çeviri henüz başlatılmadı.</summary>
    Pending,

    /// <summary>Çeviri işlemi devam ediyor.</summary>
    Translating,

    /// <summary>Çeviri başarıyla tamamlandı ve doğrulandı.</summary>
    Done,

    /// <summary>Çeviri sırasında hata oluştu (değişken uyuşmazlığı, API hatası vb.).</summary>
    Error,

    /// <summary>Bu girdi çeviri dışı bırakıldı.</summary>
    Skipped,

    /// <summary>Çeviri kullanıcı tarafından manuel olarak düzenlendi.</summary>
    ManuallyEdited
}
