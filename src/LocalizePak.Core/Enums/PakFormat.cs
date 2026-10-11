namespace LocalizePak.Core.Enums;

/// <summary>
/// Açılmış .pak arşivinin kaynak format türünü belirtir.
/// Yeniden yazma (WriteAsync) sırasında hangi serileştiricinin kullanılacağını belirler.
/// </summary>
public enum PakFormat
{
    /// <summary>Format otomatik algılanamadı veya bilinmiyor.</summary>
    Unknown = 0,

    /// <summary>Unreal Engine .pak (Magic: 0x5A6F12E1 footer tabanlı).</summary>
    Unreal = 1,

    /// <summary>Larian Studios LSPak — DOS2 / BG3 (Magic: "LSPK" 0x4B50534C).</summary>
    Larian = 2,

    /// <summary>ZIP tabanlı .pak — CryEngine / Lumberyard / Godot.</summary>
    Zip = 3,

    /// <summary>Klasik PACK — Quake / GoldSrc / id Tech.</summary>
    Classic = 4,

    /// <summary>Heuristic kurtarma modu (Format tespit edilemedi, içerik tarandı).</summary>
    Heuristic = 5
}
