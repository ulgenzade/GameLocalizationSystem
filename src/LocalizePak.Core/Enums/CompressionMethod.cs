namespace LocalizePak.Core.Enums;

/// <summary>
/// .pak arşivi içindeki dosyaların sıkıştırma yöntemini belirtir.
/// </summary>
public enum CompressionMethod
{
    None   = 0,
    Zlib   = 1,
    Gzip   = 2,
    Oodle  = 3,
    LZ4    = 4,
    Zstd   = 5
}
