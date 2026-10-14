using LocalizePak.Core.Enums;

namespace LocalizePak.Core.Models;

/// <summary>
/// Açılmış bir .pak arşivini temsil eden domain modeli.
/// </summary>
public sealed class PakArchive
{
    /// <summary>Fiziksel dosya yolu.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Dosya adı (görüntüleme amaçlı).</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Arşiv format sürümü (1–11).</summary>
    public int Version { get; set; }

    /// <summary>Arşiv AES ile şifrelenmiş mi?</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>Arşivi açmakta kullanılan AES anahtarı (hex).</summary>
    public string? AesKey { get; set; }

    /// <summary>Arşiv mount noktası (sanal dosya sistemi kökü).</summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>
    /// Arşivin kaynak format türü. WriteAsync sırasında doğru serileştiriciyi seçmek için kullanılır.
    /// </summary>
    public PakFormat Format { get; set; } = PakFormat.Unknown;

    /// <summary>Arşiv içindeki tüm dosya girdileri.</summary>
    public IReadOnlyList<PakEntry> Entries { get; set; } = [];

    /// <summary>
    /// Yalnızca gerçek lokalizasyon/metin dosyalarını döndürür.
    /// Arşivde lokalizasyon dosyası yoksa boş liste döner (ikili assetler çevrilebilir metin olarak sunulmaz).
    /// </summary>
    public IReadOnlyList<PakEntry> LocalizationEntries
    {
        get
        {
            return Entries.Where(e => e.IsLocalizationFile).ToList();
        }
    }
}
