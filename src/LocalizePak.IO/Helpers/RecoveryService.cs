using System.Text.Json;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Helpers;

public sealed class RecoverySnapshot
{
    public string SourcePath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<LocalizationEntry> Entries { get; set; } = [];
}

/// <summary>
/// Çeviri sırasında olası çökme veya elektrik kesintilerine karşı
/// arka planda otomatik ara kayıt (Snapshot) alıp geri yükleyen kurtarma servisi.
/// </summary>
public static class RecoveryService
{
    private static readonly string RecoveryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameLocalizationSystem");

    private static readonly string RecoveryFilePath = Path.Combine(RecoveryDirectory, "recovery_session.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static bool HasRecoverySnapshot()
    {
        try
        {
            return File.Exists(RecoveryFilePath) && new FileInfo(RecoveryFilePath).Length > 10;
        }
        catch
        {
            return false;
        }
    }

    public static (string SourcePath, int EntryCount, DateTime Timestamp)? GetRecoverySnapshotInfo()
    {
        try
        {
            if (!HasRecoverySnapshot()) return null;
            var json = File.ReadAllText(RecoveryFilePath);
            var snapshot = JsonSerializer.Deserialize<RecoverySnapshot>(json);
            if (snapshot == null) return null;
            return (snapshot.SourcePath, snapshot.Entries.Count, snapshot.Timestamp.ToLocalTime());
        }
        catch
        {
            return null;
        }
    }

    public static async Task SaveRecoverySnapshotAsync(string sourcePath, IReadOnlyList<LocalizationEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(RecoveryDirectory);
            var snapshot = new RecoverySnapshot
            {
                SourcePath = sourcePath,
                Timestamp = DateTime.UtcNow,
                Entries = entries.ToList()
            };
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(RecoveryFilePath, json);
        }
        catch
        {
            // Arka plan otomatik kaydetme hatası ana akışı engellememeli
        }
    }

    public static async Task<RecoverySnapshot?> LoadRecoverySnapshotAsync()
    {
        try
        {
            if (!HasRecoverySnapshot()) return null;
            var json = await File.ReadAllTextAsync(RecoveryFilePath);
            return JsonSerializer.Deserialize<RecoverySnapshot>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void ClearRecoverySnapshot()
    {
        try
        {
            if (File.Exists(RecoveryFilePath))
            {
                File.Delete(RecoveryFilePath);
            }
        }
        catch { }
    }
}
