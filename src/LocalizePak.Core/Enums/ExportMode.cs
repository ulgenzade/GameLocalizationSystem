namespace LocalizePak.Core.Enums;

/// <summary>
/// .pak dosyası dışa aktarma / kaydetme modları.
/// </summary>
public enum ExportMode
{
    /// <summary>
    /// Orijinal .pak dosyasının üzerine yazar veya aynı adla dışa aktarır (Örn: English.pak değiştirme).
    /// </summary>
    DirectReplace,

    /// <summary>
    /// Oyun motorunun ek yama kurallarını kullanarak ayrı yama dosyası üretir (Örn: OyunAdı_P.pak).
    /// </summary>
    PatchArchive,

    /// <summary>
    /// Klasör hiyerarşisiyle (Data/..., DefEd/... gibi) dışa aktarır.
    /// </summary>
    PreserveFolderStructure
}
