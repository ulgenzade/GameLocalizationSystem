namespace LocalizePak.Core.Models;

/// <summary>
/// AES Anahtar Havuzu (Key Vault) veritabanı girdisi.
/// </summary>
public record AesKeyVaultEntry
{
    public string GameName { get; init; } = string.Empty;
    public string AesKey { get; init; } = string.Empty;
    public string EngineVersion { get; init; } = "UE4 / UE5";
    public string PakPattern { get; init; } = string.Empty;
    public string Guid { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool IsCustom { get; init; } = false;
}
