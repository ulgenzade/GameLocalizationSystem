using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Uygulama ayarlarını ve API anahtarlarını yerel JSON dosyasında saklayan servis.
/// Windows'ta "...ApiKey" alanları DPAPI (CurrentUser) ile şifrelenerek yazılır; dosya başka bir
/// kullanıcı hesabına / bilgisayara kopyalansa bile anahtarlar okunamaz. Eski düz metin ayarlar
/// sorunsuz okunur ve bir sonraki kayıtta şifrelenir.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LocalizePak");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>Şifrelenmiş değerlerin öneki (düz metin anahtarlarla karışmaması için).</summary>
    public const string ProtectedPrefix = "dpapi:";

    private static readonly byte[] Entropy = "GLS-GameLocalizationSystem/settings"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Ayarları yükler. Dosya yoksa varsayılan ayarları döndürür.
    /// Dosya bozuksa yedeklenir (settings.json.corrupt-*) ki bir sonraki kayıt kullanıcının anahtarlarını sessizce silmesin.
    /// </summary>
    public static async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(SettingsFilePath))
            return new AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(SettingsFilePath);
            return DeserializeAndUnprotect(json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            BackupCorruptFile();
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    private static readonly object FileLock = new();

    /// <summary>
    /// Ayarları diske atomik ve güvenli olarak kaydeder.
    /// </summary>
    /// <returns>Kayıt başarılıysa true; aksi halde false (çağıran kullanıcıya bildirmelidir).</returns>
    public static async Task<bool> SaveAsync(AppSettings settings)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = SerializeAndProtect(settings);
            tempPath = $"{SettingsFilePath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(tempPath, json);

            lock (FileLock)
            {
                File.Move(tempPath, SettingsFilePath, overwrite: true);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return false;
        }
    }

    /// <summary>Ayarları JSON'a çevirir; "...ApiKey" alanlarını (Windows'ta) şifreler.</summary>
    public static string SerializeAndProtect(AppSettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, JsonOptions)!.AsObject();
        foreach (var name in ApiKeyPropertyNames(node))
        {
            if (node[name]?.GetValue<string>() is { Length: > 0 } plain && !plain.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                node[name] = Protect(plain);
        }
        return node.ToJsonString(JsonOptions);
    }

    /// <summary>JSON'u ayarlara çevirir; şifreli "...ApiKey" alanlarını çözer. Düz metin (eski) değerler olduğu gibi okunur.</summary>
    public static AppSettings DeserializeAndUnprotect(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("Ayar dosyası boş.");
        foreach (var name in ApiKeyPropertyNames(node))
        {
            if (node[name]?.GetValue<string>() is { } value && value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                node[name] = Unprotect(value);
        }
        return node.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
    }

    private static List<string> ApiKeyPropertyNames(JsonObject node) =>
        node.Where(p => p.Key.EndsWith("ApiKey", StringComparison.Ordinal) && p.Value is JsonValue)
            .Select(p => p.Key)
            .ToList();

    private static string Protect(string plain)
    {
        if (!OperatingSystem.IsWindows())
            return plain; // DPAPI yalnızca Windows'ta; diğer platformlarda dosya kullanıcı profilinde düz kalır.

        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(cipher);
    }

    private static string Unprotect(string stored)
    {
        if (!OperatingSystem.IsWindows())
            return string.Empty;

        try
        {
            var cipher = Convert.FromBase64String(stored[ProtectedPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Başka kullanıcı/bilgisayardan kopyalanmış ayar: anahtar çözülemez, kullanıcı yeniden girmeli.
            return string.Empty;
        }
    }

    private static void BackupCorruptFile()
    {
        try
        {
            var backup = $"{SettingsFilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(SettingsFilePath, backup, overwrite: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
