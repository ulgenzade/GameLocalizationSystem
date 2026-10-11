namespace LocalizePak.Core.Enums;

/// <summary>
/// .locres dosya biçiminin sürümünü belirtir.
/// </summary>
public enum LocresVersion : byte
{
    /// <summary>UE4 erken sürümleri — UTF-16LE, CRC32 hash.</summary>
    V1 = 1,

    /// <summary>UE4 orta sürümleri — UTF-8, CityHash64.</summary>
    V2 = 2,

    /// <summary>UE4/UE5 güncel sürümleri — UTF-8, CityHash64 + ek metadata.</summary>
    V3 = 3
}
