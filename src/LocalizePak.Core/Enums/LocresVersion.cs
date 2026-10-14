namespace LocalizePak.Core.Enums;

/// <summary>
/// .locres dosya biçiminin sürümünü belirtir.
/// </summary>
public enum LocresVersion : byte
{
    /// <summary>ELocResVersion::Legacy — magic GUID yok, metinler dosya içinde satır içi.</summary>
    Legacy = 0,

    /// <summary>ELocResVersion::Compact — yerelleştirilmiş metinler dosya sonundaki diziden indekslenir.</summary>
    V1 = 1,

    /// <summary>ELocResVersion::Optimized_CRC32 — Compact + girdi sayısı ve metin başına referans sayacı.</summary>
    V2 = 2,

    /// <summary>ELocResVersion::Optimized_CityHash64_UTF16 — UE 4.25+; namespace/key için 32-bit hash önekli (Poppy Playtime bu sürümü kullanır).</summary>
    V3 = 3
}
