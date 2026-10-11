using LocalizePak.Core.Enums;

namespace LocalizePak.Core.Models;

/// <summary>
/// Tek bir .pak arşiv girdisini (dosyasını) temsil eder.
/// </summary>
public sealed class PakEntry
{
    /// <summary>Arşiv içindeki sanal dosya yolu.</summary>
    public string VirtualPath { get; set; } = string.Empty;

    /// <summary>Veri bloğunun arşiv içindeki byte ofseti.</summary>
    public long DataOffset { get; set; }

    /// <summary>Sıkıştırılmamış veri boyutu.</summary>
    public long UncompressedSize { get; set; }

    /// <summary>Sıkıştırılmış veri boyutu (sıkıştırma yoksa UncompressedSize ile aynı).</summary>
    public long CompressedSize { get; set; }

    /// <summary>Kullanılan sıkıştırma yöntemi.</summary>
    public CompressionMethod Compression { get; set; } = CompressionMethod.None;

    /// <summary>Bu girdinin AES ile şifrelenip şifrelenmediği.</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>SHA1 veya SHA256 hash değeri (bütünlük doğrulama).</summary>
    public byte[]? Hash { get; set; }

    /// <summary>
    /// Larian Studios "Solid Archive" formatı bayrağı (Flags bit 6 = 0x40).
    /// True ise tüm dosyalar tek büyük bir solid LZ4 akışında paketlenmiştir;
    /// DataOffset solid stream içindeki RAW byte konumudur, ayrı compressed blob değildir.
    /// </summary>
    public bool IsSolid { get; set; }

    /// <summary>
    /// Solid archive formatında: solid stream'in disk üzerindeki toplam sıkıştırılmış boyutu.
    /// Solid stream başlangıç ofseti her zaman 0'dır, boyut dosya listesi ofsetine kadardır.
    /// </summary>
    public long SolidStreamSize { get; set; }

    /// <summary>Uzantıya göre bu girdinin lokalizasyon/metin dosyası olup olmadığı.</summary>
    public bool IsLocalizationFile =>
        VirtualPath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".locmeta", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".lsx", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".lang", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".strings", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".po", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase);
}
