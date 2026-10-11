using System.Text.Json;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Uygulama ayarlarını ve API anahtarlarını yerel JSON dosyasında saklayan servis.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LocalizePak");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Ayarları yükler. Dosya yoksa varsayılan ayarları döndürür.
    /// </summary>
    public static async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();

            var json = await File.ReadAllTextAsync(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static readonly object FileLock = new();

    /// <summary>
    /// Ayarları diske atomik ve güvenli olarak kaydeder.
    /// </summary>
    public static async Task SaveAsync(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = $"{SettingsFilePath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(tempPath, json);

            lock (FileLock)
            {
                File.Move(tempPath, SettingsFilePath, overwrite: true);
            }
        }
        catch
        {
            // Sessizce geç veya logla
        }
    }
}
