using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats.Unity;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Unity oyun dosyaları: UnityFS AssetBundle (.bundle, .unity3d, Addressables) ve SerializedFile
/// (resources.assets, sharedassets*.assets, level*). Metin taşıyan nesneler (TextAsset, TypeTree'li MonoBehaviour)
/// arşiv girdisi olarak listelenir; çeviri geri yazılırken yalnızca değişen nesneler özgün dosyada yamalanır.
/// </summary>
public static class UnityPakHandler
{
    private static readonly object CacheLock = new();
    private static UnityTextArchive? _cached;
    private static string? _cachedPath;
    private static DateTime _cachedStamp;

    public static bool IsUnityPak(Stream stream) => UnityTextArchive.CanOpen(stream);

    /// <summary>Uzantısı olmayan Unity dosyalarını (level0, globalgamemanagers) da içerik imzasından tanır.</summary>
    public static bool IsUnityFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return UnityTextArchive.CanOpen(fs);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static Task<PakArchive> ReadArchiveAsync(string filePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var archive = GetArchive(filePath);
            var entries = archive.Items.Select(i => new PakEntry
            {
                VirtualPath = i.VirtualPath,
                UncompressedSize = i.SizeHint,
                CompressedSize = i.SizeHint,
                Compression = CompressionMethod.None
            }).ToList();

            return new PakArchive
            {
                FilePath = filePath,
                Format = PakFormat.Unity,
                IsEncrypted = false,
                MountPoint = $"Unity {archive.UnityVersion}",
                Entries = entries
            };
        }, ct);
    }

    public static Task<Stream> ExtractEntryAsync(string filePath, PakEntry entry, CancellationToken ct = default)
    {
        return Task.Run<Stream>(() =>
        {
            lock (CacheLock)
            {
                var archive = GetArchive(filePath);
                if (!archive.TryGetItem(entry.VirtualPath, out var item))
                    throw new FileNotFoundException($"Unity dosyasında girdi bulunamadı: {entry.VirtualPath}");
                return new MemoryStream(archive.ReadItem(item));
            }
        }, ct);
    }

    /// <summary>
    /// Özgün Unity dosyasını (<paramref name="sourcePath"/>) değişen girdilerle yamalayıp <paramref name="outputPath"/>'e yazar.
    /// Çıktı kaynakla aynı yolsa önce geçici dosyaya yazılır, kaynak kapatılır, sonra yerine taşınır.
    /// </summary>
    /// <returns>Değiştirilen nesne sayısı.</returns>
    public static Task<int> WriteArchiveAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            lock (CacheLock)
            {
                InvalidateCache();
                bool inPlace = Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase);
                var target = inPlace ? outputPath + ".gls-new" : outputPath;

                int changed;
                using (var archive = UnityTextArchive.Open(sourcePath))
                {
                    changed = archive.Save(target, files);
                }

                if (inPlace)
                    File.Move(target, outputPath, overwrite: true);
                return changed;
            }
        }, ct);
    }

    private static UnityTextArchive GetArchive(string filePath)
    {
        lock (CacheLock)
        {
            var full = Path.GetFullPath(filePath);
            var stamp = File.GetLastWriteTimeUtc(full);
            if (_cached != null && _cachedPath == full && _cachedStamp == stamp)
                return _cached;

            InvalidateCache();
            _cached = UnityTextArchive.Open(full);
            _cachedPath = full;
            _cachedStamp = stamp;
            return _cached;
        }
    }

    private static void InvalidateCache()
    {
        _cached?.Dispose();
        _cached = null;
        _cachedPath = null;
    }
}
