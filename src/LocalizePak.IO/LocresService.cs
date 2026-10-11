using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.IO;

/// <summary>
/// .locres (Text Localization Resource) binary formatını ayrıştıran ve yazan servis.
/// V1 (UTF-16LE/CRC32), V2 ve V3 (UTF-8/CityHash64) sürümlerini destekler.
/// </summary>
public sealed class LocresService : ILocresSerializer
{
    // .locres Legacy Magic GUID: {0E147475-674A-03FC-4B8F-3AD9E3C4E65B}
    private static readonly byte[] LocresMagicLegacy =
    [
        0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC,
        0x4B, 0x8F, 0x3A, 0xD9, 0xE3, 0xC4, 0xE6, 0x5B
    ];

    // .locres Optimized Magic GUID: {0E147475-674A-03FC-4A15-909DC3377F1B}
    private static readonly byte[] LocresMagicOptimized =
    [
        0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC,
        0x4A, 0x15, 0x90, 0x9D, 0xC3, 0x37, 0x7F, 0x1B
    ];

    /// <inheritdoc/>
    public async Task<LocresFile> DeserializeAsync(Stream stream, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.Length < 16)
            throw new InvalidDataException("Dosya çok küçük, geçerli bir .locres dosyası değil.");

        // 1. CUE4Parse FTextLocalizationResource ile evrensel ayrıştırma (Tüm UE4/UE5 sürümleri)
        try
        {
            var cueArchive = new CUE4Parse.UE4.Readers.FByteArchive("LocresData.locres", bytes);
            var cueLocres = new CUE4Parse.UE4.Localization.FTextLocalizationResource(cueArchive);
            if (cueLocres.Entries.Count > 0)
            {
                var file = new LocresFile { Version = LocresVersion.V3 };
                foreach (var nsKvp in cueLocres.Entries)
                {
                    var ns = new LocresNamespace { Name = nsKvp.Key.Str ?? string.Empty };
                    foreach (var entryKvp in nsKvp.Value)
                    {
                        ns.Entries.Add(new LocalizationEntry
                        {
                            Key = entryKvp.Key.Str ?? string.Empty,
                            KeyHash = (uint)(entryKvp.Key.Str?.GetHashCode() ?? 0),
                            SourceText = entryKvp.Value.LocalizedString ?? string.Empty,
                            TranslatedText = entryKvp.Value.LocalizedString ?? string.Empty,
                            NamespaceName = ns.Name
                        });
                    }
                    file.Namespaces.Add(ns);
                }
                return file;
            }
        }
        catch
        {
            // CUE4Parse ayrıştıramazsa yerel geri dönüş mantığına geç
        }

        // 2. Klasik Yerel Ayrıştırıcı (Geri Dönüş)
        using var br = new BinaryReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, leaveOpen: false);
        return Deserialize(br);
    }

    /// <inheritdoc/>
    public Task<byte[]> SerializeAsync(LocresFile locresFile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        Serialize(bw, locresFile);
        return Task.FromResult(ms.ToArray());
    }

    // ─── Deserialization ─────────────────────────────────────────────────────

    private static LocresFile Deserialize(BinaryReader br)
    {
        // Magic doğrulama (Hem Legacy hem Optimized CityHash GUID kabul edilir)
        var magic = br.ReadBytes(16);
        bool isLegacy = magic.SequenceEqual(LocresMagicLegacy);
        bool isOptimized = magic.SequenceEqual(LocresMagicOptimized);
        if (!isLegacy && !isOptimized)
            throw new InvalidDataException("Geçersiz .locres magic. Bu bir lokalizasyon dosyası değil.");

        var version = (LocresVersion)br.ReadByte();
        var locres = new LocresFile { Version = version };

        // V2+ string table (isim dizisi)
        string[] nameTable = [];
        if (version >= LocresVersion.V2)
        {
            int nameCount = br.ReadInt32();
            if (nameCount < 0 || nameCount > 5_000_000)
                throw new InvalidDataException($"Geçersiz .locres isim tablosu boyutu: {nameCount}");

            nameTable = new string[nameCount];
            for (int i = 0; i < nameCount; i++)
                nameTable[i] = ReadLocresString(br, version);
        }

        // Namespace sayısı
        int nsCount = br.ReadInt32();
        if (nsCount < 0 || nsCount > 1_000_000)
            throw new InvalidDataException($"Geçersiz .locres namespace sayısı: {nsCount}");

        for (int nsIdx = 0; nsIdx < nsCount; nsIdx++)
        {
            var ns = new LocresNamespace
            {
                Name = ReadLocresString(br, version)
            };

            // Namespace hash (V2+ için key hash'inden önce)
            if (version >= LocresVersion.V2)
                br.ReadUInt32(); // namespace hash — kullanılmaz

            int entryCount = br.ReadInt32();
            if (entryCount < 0 || entryCount > 5_000_000)
                throw new InvalidDataException($"Geçersiz .locres girdi sayısı: {entryCount}");

            for (int eIdx = 0; eIdx < entryCount; eIdx++)
            {
                string key = ReadLocresString(br, version);
                uint keyHash = br.ReadUInt32();
                string sourceText = ReadLocresString(br, version);

                ns.Entries.Add(new Core.Models.LocalizationEntry
                {
                    Key = key,
                    KeyHash = keyHash,
                    SourceText = sourceText,
                    TranslatedText = sourceText, // Başlangıçta kaynak = hedef
                    NamespaceName = ns.Name
                });
            }

            locres.Namespaces.Add(ns);
        }

        return locres;
    }

    // ─── Serialization ───────────────────────────────────────────────────────

    private static void Serialize(BinaryWriter bw, LocresFile locres)
    {
        // Magic (V3 ise Optimized, değilse Legacy)
        bw.Write(locres.Version == LocresVersion.V3 ? LocresMagicOptimized : LocresMagicLegacy);
        bw.Write((byte)locres.Version);

        // V2+ string table (sadece basit bir isim listesi yaz)
        if (locres.Version >= LocresVersion.V2)
        {
            var allStrings = CollectUniqueStrings(locres);
            bw.Write(allStrings.Count);
            foreach (var s in allStrings)
                WriteLocresString(bw, s, locres.Version);
        }

        // Namespace'leri yaz
        bw.Write(locres.Namespaces.Count);
        foreach (var ns in locres.Namespaces)
        {
            WriteLocresString(bw, ns.Name, locres.Version);

            if (locres.Version >= LocresVersion.V2)
                bw.Write(ComputeKeyHash(ns.Name)); // namespace hash

            bw.Write(ns.Entries.Count);
            foreach (var entry in ns.Entries)
            {
                WriteLocresString(bw, entry.Key, locres.Version);
                bw.Write(entry.KeyHash); // Orijinal hash korunuyor!
                // Çevrilmiş metni yaz (kaynak yerine)
                var targetText = string.IsNullOrEmpty(entry.TranslatedText)
                    ? entry.SourceText
                    : entry.TranslatedText;
                WriteLocresString(bw, targetText, locres.Version);
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string ReadLocresString(BinaryReader br, LocresVersion version)
    {
        int length = br.ReadInt32();
        if (length == 0) return string.Empty;

        // Unreal FString: negatif uzunluk = UTF-16LE, pozitif uzunluk = UTF-8
        if (version == LocresVersion.V1 || length < 0)
        {
            if (length == int.MinValue)
                throw new InvalidDataException("Geçersiz dize uzunluğu (int.MinValue)");

            int charCount = Math.Abs(length);
            if (charCount > 50_000_000)
                throw new InvalidDataException($"Dize boyutu izin verilen sınırı aşıyor: {charCount}");

            var bytes = br.ReadBytes(charCount * 2);
            return System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        else
        {
            if (length > 100_000_000)
                throw new InvalidDataException($"Dize boyutu izin verilen sınırı aşıyor: {length}");

            var bytes = br.ReadBytes(length);
            return System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
    }

    private static void WriteLocresString(BinaryWriter bw, string value, LocresVersion version)
    {
        if (string.IsNullOrEmpty(value))
        {
            bw.Write(0);
            return;
        }

        if (version == LocresVersion.V1)
        {
            var bytes = System.Text.Encoding.Unicode.GetBytes(value + '\0');
            bw.Write(-bytes.Length / 2); // Negatif = Unicode
            bw.Write(bytes);
        }
        else
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value + '\0');
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }
    }

    private static List<string> CollectUniqueStrings(LocresFile locres)
    {
        var set = new HashSet<string>();
        foreach (var ns in locres.Namespaces)
        {
            set.Add(ns.Name);
            foreach (var e in ns.Entries)
                set.Add(e.Key);
        }
        return [.. set];
    }

    /// <summary>
    /// Basit CRC32 tabanlı key hash hesaplama.
    /// Not: Gerçek oyunlar CityHash64 kullanabilir; bu implementasyon V1 uyumludur.
    /// V2/V3'te orijinal hash değerleri korunur (değiştirilmez).
    /// </summary>
    private static uint ComputeKeyHash(string key)
    {
        // CRC32 implementasyonu (IEEE 802.3 polynomial)
        uint crc = 0xFFFFFFFF;
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        foreach (var b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
