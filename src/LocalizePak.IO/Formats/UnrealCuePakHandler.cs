using System.Collections.Concurrent;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Unreal Engine (V1–V11+ / UE4 & UE5) paketlerini Oodle, LZ4, Zlib ve AES-256 desteğiyle
/// evrensel düzeyde ayrıştıran ve çıkaran motor.
/// </summary>
public static class UnrealCuePakHandler
{
    public const uint PakMagic = 0x5A6F12E1;

    // Performans için aktif provider önbelleği (aynı pak dosyasından ardışık dosya çıkarmaları hızlandırmak için)
    private static readonly ConcurrentDictionary<string, (DefaultFileProvider Provider, DateTime LastUsed)> _cache = new();

    private static bool _oodleInitialized = false;
    private static readonly object _oodleLock = new();

    /// <summary>
    /// Oodle sıkıştırma motorunu (oo2core_9_win64.dll) otomatik tespit eder veya yükler.
    /// </summary>
    public static void EnsureOodle()
    {
        if (_oodleInitialized) return;
        lock (_oodleLock)
        {
            if (_oodleInitialized) return;
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "oo2core_9_win64.dll"),
                    Path.Combine(baseDir, "Native", "oo2core_9_win64.dll"),
                    Path.Combine(baseDir, "runtimes", "win-x64", "native", "oo2core_9_win64.dll"),
                    Path.Combine(Directory.GetCurrentDirectory(), "oo2core_9_win64.dll"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Native", "oo2core_9_win64.dll")
                };

                string? foundPath = candidates.FirstOrDefault(File.Exists);
                if (foundPath != null)
                {
                    CUE4Parse.Compression.OodleHelper.Initialize(foundPath);
                    _oodleInitialized = true;
                    return;
                }

                var targetPath = Path.Combine(baseDir, "oo2core_9_win64.dll");
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var bytes = client.GetByteArrayAsync("https://github.com/new-world-tools/go-oodle/releases/download/v0.2.1-file/oo2core_9_win64.dll").GetAwaiter().GetResult();
                File.WriteAllBytes(targetPath, bytes);
                CUE4Parse.Compression.OodleHelper.Initialize(targetPath);
                _oodleInitialized = true;
            }
            catch
            {
                // Oodle harici formatlar (Zlib, LZ4) çalışmaya devam edebilir
            }
        }
    }

    /// <summary>
    /// Verilen dosyanın Unreal Engine .pak arşivi olup olmadığını tespit eder.
    /// </summary>
    public static bool IsUnrealPak(Stream stream)
    {
        if (stream.Length < 44) return false;
        long originalPos = stream.Position;
        try
        {
            long scanSize = Math.Min(stream.Length, 8192);
            stream.Seek(-scanSize, SeekOrigin.End);
            var buffer = new byte[scanSize];
            int read = stream.Read(buffer, 0, (int)scanSize);

            for (int i = read - 4; i >= 0; i--)
            {
                uint candidate = BitConverter.ToUInt32(buffer, i);
                if (candidate == PakMagic)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    /// <summary>
    /// Unreal Engine .pak dosyasını ayrıştırır ve PakArchive nesnesi döndürür.
    /// </summary>
    public static async Task<PakArchive> ReadArchiveAsync(
        string filePath,
        string? aesKey = null,
        CancellationToken ct = default)
    {
        await Task.Yield();
        EnsureOodle();
        var fileInfo = new FileInfo(filePath);
        var dir = fileInfo.DirectoryName ?? Directory.GetCurrentDirectory();
        var fileName = fileInfo.Name;

        // CUE4Parse FileProvider başlat
        var provider = new DefaultFileProvider(dir, SearchOption.TopDirectoryOnly, false, new VersionContainer(EGame.GAME_UE4_27));
        provider.Initialize();

        bool isEncrypted = false;
        if (!string.IsNullOrWhiteSpace(aesKey))
        {
            try
            {
                var key = new FAesKey(aesKey.Trim());
                provider.SubmitKey(new FGuid(), key);
                isEncrypted = true;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Geçersiz AES anahtarı: {ex.Message}", ex);
            }
        }

        var entries = new List<PakEntry>();
        string mountPoint = string.Empty;

        foreach (var vfs in provider.MountedVfs)
        {
            if (!string.Equals(vfs.Name, fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            mountPoint = vfs.MountPoint;
            foreach (var kvp in vfs.Files)
            {
                ct.ThrowIfCancellationRequested();
                var vfsFile = kvp.Value;
                entries.Add(new PakEntry
                {
                    VirtualPath = kvp.Key.Replace('\\', '/'),
                    UncompressedSize = vfsFile.Size,
                    CompressedSize = vfsFile.Size,
                    IsEncrypted = vfsFile.IsEncrypted,
                    DataOffset = 0,
                    Compression = CompressionMethod.None
                });
            }
        }

        if (entries.Count == 0 && isEncrypted)
        {
            // Şifreli olup anahtar girilmemiş veya eşleşmemiş olabilir
            throw new InvalidOperationException("Arşiv şifreli veya geçerli AES anahtarı ile çözülemedi.");
        }

        // Önbelleğe kaydet (ExtractEntryAsync için)
        _cache[filePath] = (provider, DateTime.UtcNow);

        return new PakArchive
        {
            FilePath = filePath,
            Format = PakFormat.Unreal,
            Version = 11,
            IsEncrypted = isEncrypted,
            AesKey = aesKey,
            MountPoint = mountPoint,
            Entries = entries
        };
    }

    /// <summary>
    /// PakArchive içindeki tek bir girdiyi deşifre edilmiş ve açılmış olarak çıkarır.
    /// </summary>
    public static async Task<Stream> ExtractEntryAsync(
        string filePath,
        PakEntry entry,
        string? aesKey = null,
        CancellationToken ct = default)
    {
        await Task.Yield();
        EnsureOodle();
        DefaultFileProvider provider;

        if (_cache.TryGetValue(filePath, out var cached) && (DateTime.UtcNow - cached.LastUsed).TotalMinutes < 10)
        {
            provider = cached.Provider;
            _cache[filePath] = (provider, DateTime.UtcNow);
        }
        else
        {
            var fileInfo = new FileInfo(filePath);
            var dir = fileInfo.DirectoryName ?? Directory.GetCurrentDirectory();
            provider = new DefaultFileProvider(dir, SearchOption.TopDirectoryOnly, false, new VersionContainer(EGame.GAME_UE4_27));
            provider.Initialize();
            if (!string.IsNullOrWhiteSpace(aesKey))
            {
                provider.SubmitKey(new FGuid(), new FAesKey(aesKey.Trim()));
            }
            _cache[filePath] = (provider, DateTime.UtcNow);
        }

        // 1. Doğrudan Files dictionary üzerinden bul ve GameFile.Read() ile oku
        if (provider.Files.TryGetValue(entry.VirtualPath, out var gameFile))
        {
            var data = gameFile.Read();
            return new MemoryStream(data);
        }

        // 2. Case-insensitive ara
        var match = provider.Files.FirstOrDefault(kvp => string.Equals(kvp.Key, entry.VirtualPath, StringComparison.OrdinalIgnoreCase));
        if (match.Value != null)
        {
            var data = match.Value.Read();
            return new MemoryStream(data);
        }

        // 3. MountedVfs içinde ara
        foreach (var vfs in provider.MountedVfs)
        {
            var fileMatch = vfs.Files.FirstOrDefault(kvp => string.Equals(kvp.Key, entry.VirtualPath, StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(kvp.Value.Path, entry.VirtualPath, StringComparison.OrdinalIgnoreCase));
            if (fileMatch.Value != null)
            {
                var data = fileMatch.Value.Read();
                return new MemoryStream(data);
            }
        }

        throw new FileNotFoundException($"Paket içindeki dosya çıkarılamadı: {entry.VirtualPath}");
    }
}
