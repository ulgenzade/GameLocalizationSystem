using System.Collections.Concurrent;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats.IoStore;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Unreal Engine (V1–V12 / UE4 & UE5) paketlerini ve UE5 IoStore konteynerlerini (.utoc/.ucas) Oodle, LZ4, Zlib ve
/// AES-256 desteğiyle ayrıştıran ve çıkaran motor. Aynı adlı .pak ve .utoc tek bir arşiv olarak gösterilir.
/// </summary>
public static class UnrealCuePakHandler
{
    public const uint PakMagic = 0x5A6F12E1;

    // Performans için aktif provider önbelleği (aynı pak dosyasından ardışık dosya çıkarmaları hızlandırmak için)
    private static readonly ConcurrentDictionary<string, (DefaultFileProvider Provider, DateTime LastUsed)> _cache = new();

    private static bool _oodleInitialized = false;
    private static readonly object _oodleLock = new();

    /// <summary>Son Oodle yükleme denemesinin sonucu (başarılıysa "ok: yol"; değilse nedeni).</summary>
    public static string? OodleStatus { get; private set; }

    /// <summary>
    /// Oodle sıkıştırma kütüphanesini yükler. Aranan yerler: GLS_OODLE_PATH ortam değişkeni, uygulama klasörü,
    /// Native/ alt klasörü, oyun klasörü ve çalışma dizini. Üçüncü taraf bir adresten otomatik indirme YALNIZCA
    /// GLS_ALLOW_OODLE_DOWNLOAD=1 ayarlandığında yapılır.
    /// </summary>
    public static void EnsureOodle(string? pakOrGamePath = null)
    {
        if (_oodleInitialized) return;
        lock (_oodleLock)
        {
            if (_oodleInitialized) return;
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var cwd = Directory.GetCurrentDirectory();
                var names = new[] { "oo2core_9_win64.dll", "oo2core_8_win64.dll", "liboodle-data-shared.so", "liboo2corelinux64.so.9", "liboodle-data-shared.dylib" };
                var candidates = new List<string>();

                var env = Environment.GetEnvironmentVariable("GLS_OODLE_PATH");
                if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);

                foreach (var dir in new[] { baseDir, Path.Combine(baseDir, "Native"), Path.Combine(baseDir, "runtimes", "win-x64", "native"), cwd, Path.Combine(cwd, "Native") })
                    foreach (var n in names)
                        candidates.Add(Path.Combine(dir, n));

                // Eğer hedef paket veya oyun yolu verildiyse, oyunun klasöründeki Oodle DLL'ini ara
                if (!string.IsNullOrEmpty(pakOrGamePath))
                {
                    var startDir = File.Exists(pakOrGamePath) ? Path.GetDirectoryName(pakOrGamePath) : pakOrGamePath;
                    var cur = !string.IsNullOrEmpty(startDir) && Directory.Exists(startDir) ? new DirectoryInfo(startDir) : null;
                    // Yalnızca pak klasörü ve 4 üst klasör + UE'nin bilinen Oodle konumları taranır.
                    // (Eskiden her üst klasörde AllDirectories ile arama yapılıyordu: Steam kütüphanesi / tüm
                    // sürücü taranabiliyor, uygulama donuyor ve ilgisiz bir klasördeki DLL yüklenebiliyordu.)
                    string[] knownSubDirs = ["", Path.Combine("Binaries", "Win64"), Path.Combine("Engine", "Binaries", "ThirdParty", "Oodle", "Win64")];
                    for (int i = 0; i < 5 && cur != null; i++)
                    {
                        foreach (var sub in knownSubDirs)
                        {
                            try
                            {
                                var dir = Path.Combine(cur.FullName, sub);
                                if (!Directory.Exists(dir)) continue;
                                foreach (var d in Directory.EnumerateFiles(dir, "oo2core*.dll", SearchOption.TopDirectoryOnly))
                                    candidates.Add(d);
                            }
                            catch (UnauthorizedAccessException) { }
                            catch (IOException) { }
                        }
                        cur = cur.Parent;
                    }
                }

                string? foundPath = candidates.FirstOrDefault(File.Exists);
                if (foundPath != null)
                {
                    CUE4Parse.Compression.OodleHelper.Initialize(foundPath);
                    _oodleInitialized = true;
                    OodleStatus = $"ok: {foundPath}";
                    return;
                }

                if (Environment.GetEnvironmentVariable("GLS_ALLOW_OODLE_DOWNLOAD") == "1")
                {
                    var targetPath = Path.Combine(baseDir, "oo2core_9_win64.dll");
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    var bytes = client.GetByteArrayAsync("https://github.com/new-world-tools/go-oodle/releases/download/v0.2.1-file/oo2core_9_win64.dll").GetAwaiter().GetResult();
                    File.WriteAllBytes(targetPath, bytes);
                    CUE4Parse.Compression.OodleHelper.Initialize(targetPath);
                    _oodleInitialized = true;
                    OodleStatus = $"ok (indirildi): {targetPath}";
                    return;
                }

                OodleStatus = "Oodle kütüphanesi bulunamadı; oo2core_9_win64.dll dosyasını uygulamanın yanına koyun veya GLS_OODLE_PATH ayarlayın.";
            }
            catch (Exception ex)
            {
                // Oodle harici formatlar (Zlib, LZ4) çalışmaya devam edebilir; nedeni kaydet, sessizce yutma.
                OodleStatus = $"yüklenemedi: {ex.Message}";
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

    /// <summary>Akışın UE IoStore içerik tablosu (.utoc, magic "-==--==--==--==-") olup olmadığı.</summary>
    public static bool IsIoStoreToc(Stream stream) => ReadTocVersion(stream) >= 0;

    /// <summary>.utoc başlığındaki sürüm baytını (EIoStoreTocVersion) okur; .utoc değilse -1.</summary>
    public static int ReadTocVersion(Stream stream)
    {
        if (stream.Length < 144) return -1;
        long originalPos = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> head = stackalloc byte[17];
            stream.ReadExactly(head);
            return head[..16].SequenceEqual("-==--==--==--==-"u8) ? head[16] : -1;
        }
        catch
        {
            return -1;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    /// <summary>.pak veya .utoc yolu için aynı ada sahip .utoc dosyası (IoStore konteyneri) varsa yolu.</summary>
    public static string? FindCompanionToc(string filePath)
    {
        var toc = Path.ChangeExtension(filePath, ".utoc");
        return File.Exists(toc) ? toc : null;
    }

    /// <summary>
    /// IoStore TOC sürümünü yaklaşık motor sürümüne eşler (konteynerin hangi UE sürümüyle pişirildiğinin en iyi göstergesi).
    /// </summary>
    public static EGame MapTocVersionToEGame(int tocVersion) => (EIoStoreTocVersion)tocVersion switch
    {
        <= EIoStoreTocVersion.Initial => EGame.GAME_UE4_25,
        EIoStoreTocVersion.DirectoryIndex => EGame.GAME_UE4_26,
        EIoStoreTocVersion.PartitionSize => EGame.GAME_UE4_27,
        EIoStoreTocVersion.PerfectHash => EGame.GAME_UE5_0,
        EIoStoreTocVersion.PerfectHashWithOverflow => EGame.GAME_UE5_1,
        EIoStoreTocVersion.OnDemandMetaData => EGame.GAME_UE5_2,
        EIoStoreTocVersion.RemovedOnDemandMetaData => EGame.GAME_UE5_3,
        EIoStoreTocVersion.ReplaceIoChunkHashWithIoHash => EGame.GAME_UE5_4,
        EIoStoreTocVersion.AddedSourceHashes => EGame.GAME_UE5_5,
        _ => EGame.GAME_UE5_LATEST
    };

    /// <summary>Varsayılan Unreal Engine sürümü (ayarlanabilir).</summary>
    public static EGame DefaultGameVersion { get; set; } = EGame.GAME_UE4_27;

    /// <summary>
    /// .pak dosyasının footer'ındaki sürüm bilgisini (int32) okur. Bulunamazsa -1 döner.
    /// </summary>
    public static int DetectPakVersion(Stream stream)
    {
        if (stream.Length < 44) return -1;
        long originalPos = stream.Position;
        try
        {
            long scanSize = Math.Min(stream.Length, 8192);
            stream.Seek(-scanSize, SeekOrigin.End);
            var buffer = new byte[scanSize];
            int read = stream.Read(buffer, 0, (int)scanSize);

            for (int i = read - 8; i >= 0; i--)
            {
                uint candidate = BitConverter.ToUInt32(buffer, i);
                if (candidate == PakMagic)
                {
                    return BitConverter.ToInt32(buffer, i + 4);
                }
            }
            return -1;
        }
        catch
        {
            return -1;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    /// <summary>
    /// Dosya yolundan Unreal paketinin sürümünü okur ve uygun CUE4Parse EGame enum değerini saptar.
    /// </summary>
    public static EGame DetectEngineVersion(string filePath)
    {
        try
        {
            // IoStore kullanan oyunlarda .utoc sürümü motoru .pak sürümünden daha kesin belirtir
            if (FindCompanionToc(filePath) is { } tocPath)
            {
                using var toc = File.OpenRead(tocPath);
                int tocVersion = ReadTocVersion(toc);
                if (tocVersion > 0) return MapTocVersionToEGame(tocVersion);
            }

            using var fs = File.OpenRead(filePath);
            int version = DetectPakVersion(fs);
            return MapPakVersionToEGame(version);
        }
        catch
        {
            return DefaultGameVersion;
        }
    }

    /// <summary>
    /// Pak sürüm numarasını CUE4Parse EGame motor sürümüne eşler.
    /// </summary>
    public static EGame MapPakVersionToEGame(int pakVersion)
    {
        return pakVersion switch
        {
            <= 7 and > 0 => EGame.GAME_UE4_21,
            8 => EGame.GAME_UE4_25,
            9 => EGame.GAME_UE4_26,
            10 => EGame.GAME_UE4_27,
            11 => EGame.GAME_UE5_1,
            >= 12 => EGame.GAME_UE5_4,
            _ => DefaultGameVersion
        };
    }

    /// <summary>
    /// Unreal Engine .pak dosyasını ayrıştırır ve PakArchive nesnesi döndürür (geriye dönük uyumlu aşırı yükleme).
    /// </summary>
    public static Task<PakArchive> ReadArchiveAsync(
        string filePath,
        string? aesKey,
        CancellationToken ct)
        => ReadArchiveAsync(filePath, aesKey, null, ct);

    /// <summary>
    /// Unreal Engine .pak dosyasını ayrıştırır ve PakArchive nesnesi döndürür.
    /// </summary>
    public static async Task<PakArchive> ReadArchiveAsync(
        string filePath,
        string? aesKey = null,
        EGame? ueVersion = null,
        CancellationToken ct = default)
    {
        await Task.Yield();
        EnsureOodle(filePath);
        var gameVersion = ueVersion ?? DetectEngineVersion(filePath);
        var provider = CreateProvider(filePath, aesKey, gameVersion);
        bool keyGiven = !string.IsNullOrWhiteSpace(aesKey);

        var entries = new List<PakEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string mountPoint = string.Empty;
        IoStoreReader? ioStore = null;
        int pakVersion = 0;
        bool isEncrypted = false;

        // Aynı adlı .pak ve .utoc tek bir mantıksal konteynerdir (IoStore'da paketler .ucas'ta, diğer dosyalar .pak'ta)
        foreach (var vfs in ContainersOf(provider, filePath))
        {
            isEncrypted |= vfs.IsEncrypted;
            if (vfs is IoStoreReader io) ioStore = io;
            else if (vfs is CUE4Parse.UE4.Pak.PakFileReader pak) pakVersion = (int)pak.Info.Version;
            if (string.IsNullOrEmpty(mountPoint)) mountPoint = vfs.MountPoint;

            foreach (var kvp in vfs.Files)
            {
                ct.ThrowIfCancellationRequested();
                var path = kvp.Key.Replace('\\', '/');
                if (!seen.Add(path)) continue;
                var vfsFile = kvp.Value;
                entries.Add(new PakEntry
                {
                    VirtualPath = path,
                    UncompressedSize = vfsFile.Size,
                    CompressedSize = vfsFile.Size,
                    IsEncrypted = vfsFile.IsEncrypted,
                    DataOffset = 0,
                    Compression = CompressionMethod.None
                });
            }
        }

        // Şifre, arşivin gerçekte şifreli olmasına göre belirlenir; girilen (ör. Anahtar Havuzu'ndan otomatik eşleşen)
        // bir anahtar şifresiz arşivi "şifreli" yapmaz — aksi halde yama, oyunun bilmediği bir anahtarla şifrelenirdi.
        isEncrypted |= entries.Any(e => e.IsEncrypted);

        if (entries.Count == 0 && keyGiven)
        {
            // Şifreli olup anahtar girilmemiş veya eşleşmemiş olabilir
            throw new InvalidOperationException("Arşiv şifreli veya geçerli AES anahtarı ile çözülemedi.");
        }

        // Oyunun kendi .locres dosyası yoksa paketlerdeki FText/StringTable metinlerinden üretilecek Game.locres girdisi
        if (GeneratedLocresPath(entries) is { } generatedPath)
        {
            entries.Add(new PakEntry { VirtualPath = generatedPath, IsGenerated = true });
        }

        // Önbelleğe kaydet (ExtractEntryAsync için)
        _cache[filePath] = (provider, DateTime.UtcNow);

        var archive = new PakArchive
        {
            FilePath = filePath,
            Format = PakFormat.Unreal,
            Version = pakVersion > 0 ? pakVersion : 11,
            IsEncrypted = isEncrypted,
            AesKey = isEncrypted ? aesKey : null,
            MountPoint = mountPoint,
            Entries = entries
        };

        if (ioStore != null)
        {
            archive.IoStoreTocPath = ioStore.Path;
            archive.IoStoreTocVersion = (int)ioStore.TocResource.Header.Version;
            try { archive.IoStoreContainerHeaderVersion = (int)(ioStore.ContainerHeader?.Version ?? EIoContainerHeaderVersion.Initial); }
            catch { archive.IoStoreContainerHeaderVersion = (int)EIoContainerHeaderVersion.Initial; }
        }
        return archive;
    }

    /// <summary>
    /// Klasördeki tüm paketleri bağlayan sağlayıcı. Şifresiz konteynerler her zaman bağlanır (ör. şifreli bir oyunda
    /// global.utoc), anahtar verildiyse şifreli olanlar da açılır.
    /// </summary>
    private static DefaultFileProvider CreateProvider(string filePath, string? aesKey, EGame gameVersion)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Directory.GetCurrentDirectory();
        var provider = new DefaultFileProvider(dir, SearchOption.TopDirectoryOnly, new VersionContainer(gameVersion), StringComparer.OrdinalIgnoreCase);
        provider.Initialize();
        provider.Mount();

        if (!string.IsNullOrWhiteSpace(aesKey))
        {
            try
            {
                provider.SubmitKey(new FGuid(), new FAesKey(aesKey.Trim()));
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Geçersiz AES anahtarı: {ex.Message}", ex);
            }
        }
        return provider;
    }

    /// <summary>Açılan dosyayla aynı adı taşıyan bağlı konteynerler (ör. pakchunk0-Windows.pak + pakchunk0-Windows.utoc).</summary>
    private static IEnumerable<IAesVfsReader> ContainersOf(DefaultFileProvider provider, string filePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        return provider.MountedVfs
            .Where(v => string.Equals(Path.GetFileNameWithoutExtension(v.Name), baseName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v is IoStoreReader ? 1 : 0); // önce .pak (bağlama noktası ve .locres), sonra .utoc
    }

    /// <summary>
    /// Arşivde oyuna ait (motor dışı) hiç .locres yoksa ve çevrilebilir paket varsa üretilecek
    /// <c>&lt;Proje&gt;/Content/Localization/Game/en/Game.locres</c> yolu; aksi halde null.
    /// </summary>
    public static string? GeneratedLocresPath(IReadOnlyList<PakEntry> entries)
    {
        if (entries.Any(e => e.IsGameFile && e.VirtualPath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase)))
            return null;

        var package = entries.FirstOrDefault(e => IoStoreTextGatherer.IsGamePackage(e.VirtualPath));
        if (package == null) return null;

        var path = package.VirtualPath.TrimStart('/');
        int slash = path.IndexOf('/');
        if (slash <= 0) return null;
        return $"{path[..slash]}/Content/Localization/Game/en/Game.locres";
    }

    private static readonly ConcurrentDictionary<string, byte[]> _generatedLocres = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Konteynerin paketlerini tarayıp toplanan metinleri .locres olarak döndürür (arşiv başına önbelleklenir).</summary>
    private static byte[] GenerateLocres(DefaultFileProvider provider, string filePath, CancellationToken ct)
    {
        if (_generatedLocres.TryGetValue(filePath, out var cached)) return cached;

        var files = ContainersOf(provider, filePath)
            .SelectMany(v => v.Files.Values)
            .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (f.Path, (Func<byte[]>)(() => f.Read())));
        var locres = IoStoreTextGatherer.Gather(files, ct: ct);
        var bytes = LocresCodec.Write(locres);
        _generatedLocres[filePath] = bytes;
        return bytes;
    }

    /// <summary>
    /// PakArchive içindeki tek bir girdiyi deşifre edilmiş ve açılmış olarak çıkarır (geriye dönük uyumlu aşırı yükleme).
    /// </summary>
    public static Task<Stream> ExtractEntryAsync(
        string filePath,
        PakEntry entry,
        string? aesKey,
        CancellationToken ct)
        => ExtractEntryAsync(filePath, entry, aesKey, null, ct);

    /// <summary>
    /// PakArchive içindeki tek bir girdiyi deşifre edilmiş ve açılmış olarak çıkarır.
    /// </summary>
    public static async Task<Stream> ExtractEntryAsync(
        string filePath,
        PakEntry entry,
        string? aesKey = null,
        EGame? ueVersion = null,
        CancellationToken ct = default)
    {
        await Task.Yield();
        EnsureOodle(filePath);
        DefaultFileProvider provider;

        if (_cache.TryGetValue(filePath, out var cached) && (DateTime.UtcNow - cached.LastUsed).TotalMinutes < 10)
        {
            provider = cached.Provider;
            _cache[filePath] = (provider, DateTime.UtcNow);
        }
        else
        {
            provider = CreateProvider(filePath, aesKey, ueVersion ?? DetectEngineVersion(filePath));
            _cache[filePath] = (provider, DateTime.UtcNow);
        }

        if (entry.IsGenerated)
        {
            return new MemoryStream(GenerateLocres(provider, filePath, ct), writable: false);
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
