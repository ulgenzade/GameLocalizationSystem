namespace LocalizePak.Core.Models;

/// <summary>
/// Yapay zekanın oyun motoru ve AES anahtarı arama sonucu.
/// </summary>
public sealed class GameEngineAndAesResult
{
    public string GameName { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string EngineName { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string? FoundAesKey { get; set; }
    public bool HasAesKey => !string.IsNullOrWhiteSpace(FoundAesKey) && FoundAesKey.Length >= 32;
    public string SearchAdvice { get; set; } = string.Empty;
    public string SearchQuery { get; set; } = string.Empty;
    public string DirectSearchUrl => $"https://www.google.com/search?q={Uri.EscapeDataString(SearchQuery)}";
}
