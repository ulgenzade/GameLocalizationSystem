using LocalizePak.Core.Enums;

namespace LocalizePak.Core.Models;

/// <summary>
/// Bir .locres dosyasının içindeki tek bir isim alanını (namespace) temsil eder.
/// </summary>
public sealed class LocresNamespace
{
    /// <summary>İsim alanı adı.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>İsim alanı adının dosyada saklanan 32-bit hash'i (V3). Değiştirilmeden yeniden yazılır.</summary>
    public uint NameHash { get; set; }

    /// <summary>Bu isim alanına ait çeviri girdileri.</summary>
    public List<LocalizationEntry> Entries { get; set; } = [];
}

/// <summary>
/// Ayrıştırılmış bir .locres dosyasını temsil eden domain modeli.
/// </summary>
public sealed class LocresFile
{
    /// <summary>Dosya formatı sürümü.</summary>
    public LocresVersion Version { get; set; } = LocresVersion.V2;

    /// <summary>Kaynak dosya yolu (fiziksel veya sanal).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>İsim alanı koleksiyonu.</summary>
    public List<LocresNamespace> Namespaces { get; set; } = [];

    /// <summary>Tüm girdileri düzleştirilmiş liste olarak döndürür.</summary>
    public IEnumerable<LocalizationEntry> AllEntries =>
        Namespaces.SelectMany(ns => ns.Entries);

    /// <summary>Toplam girdi sayısı.</summary>
    public int TotalEntryCount => Namespaces.Sum(ns => ns.Entries.Count);
}
