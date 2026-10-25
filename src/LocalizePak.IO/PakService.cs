using System.IO.Compression;
using K4os.Compression.LZ4;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Formats.IoStore;
using LocalizePak.IO.Helpers;
using ZstdSharp;

namespace LocalizePak.IO;

/// <summary>
/// Tüm video oyunu arşiv formatlarını (Unreal Engine V1–V11+, Larian LSPak, CryEngine/ZIP, Quake PACK ve Heuristic Kurtarma)
/// otomatik algılayan, çözen ve serileştiren evrensel arşiv servisi.
/// </summary>
public sealed class PakService : IPakReader, IPakWriter
{
    public const uint PakMagic = 0x5A6F12E1;

    /// <summary>Tek bir girdi için izin verilen azami boyut (bozuk/kötü niyetli arşivlere karşı koruma).</summary>
    private const long MaxEntrySize = 1L * 1024 * 1024 * 1024; // 1 GB
    private const long MaxIndexSize = 256L * 1024 * 1024;     // 256 MB

    /// <inheritdoc/>
    public async Task<PakArchive> OpenAsync(
        string filePath,
        string? aesKey = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($".pak dosyası bulunamadı: {filePath}");

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

        // 1. Larian LSPak format kontrolü (Magic: "LSPK" = 0x4B50534C) — Baldur's Gate 3 / Divinity
        if (LSPakHandler.IsLSPak(fs))
        {
            try
            {
                var lspak = await LSPakHandler.ReadArchiveAsync(filePath, ct);
                if (lspak.Entries.Count > 0)
                {
                    lspak.Format = PakFormat.Larian;
                    return lspak;
                }
            }
            catch
            {
                // Bilinmeyen varyant durumunda diğer ayrıştırıcılara devam et
            }
        }

        // 2. CryEngine / Lumberyard / ZIP tabanlı .pak (Magic: "PK\x03\x04")
        if (ZipPakHandler.IsZipPak(fs))
        {
            var zipPak = await ZipPakHandler.ReadArchiveAsync(filePath, ct);
            zipPak.Format = PakFormat.Zip;
            return zipPak;
        }

        // 3. Godot Engine .pck / .pak (Magic: "GDPC" = 0x43504447)
        if (GodotPakHandler.IsGodotPak(fs))
        {
            try
            {
                var godotPak = await GodotPakHandler.ReadArchiveAsync(filePath, ct);
                if (godotPak.Entries.Count > 0)
                {
                    godotPak.Format = PakFormat.Godot;
                    return godotPak;
                }
            }
            catch
            {
                // Geri dönüşe devam et
            }
        }

        // 4. Unity: UnityFS AssetBundle (.bundle / .unity3d) veya SerializedFile (.assets / level*)
        if (UnityPakHandler.IsUnityPak(fs))
        {
            try
            {
                return await UnityPakHandler.ReadArchiveAsync(filePath, ct);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or EndOfStreamException)
            {
                // Sezgisel taramaya düşmek yerine (GB'lık paketlerde çok yavaş) gerçek nedeni bildir
                throw new InvalidDataException($"Unity dosyası açılamadı: {ex.Message}", ex);
            }
        }

        // 5. Classic ID Tech / Quake / Source / GoldSrc PACK (Magic: "PACK")
        if (ClassicPakHandler.IsClassicPak(fs))
        {
            var classicPak = await ClassicPakHandler.ReadArchiveAsync(filePath, ct);
            classicPak.Format = PakFormat.Classic;
            return classicPak;
        }

        // 6a. UE5 IoStore içerik tablosu (.utoc): aynı adlı .pak ile tek arşiv olarak açılır
        if (UnrealCuePakHandler.IsIoStoreToc(fs))
        {
            return await UnrealCuePakHandler.ReadArchiveAsync(filePath, aesKey, ct);
        }

        // 6. Unreal Engine .pak (V1–V12 / UE4 & UE5, Oodle, LZ4, Zlib, AES-256)
        if (UnrealCuePakHandler.IsUnrealPak(fs))
        {
            try
            {
                var unrealPak = await UnrealCuePakHandler.ReadArchiveAsync(filePath, aesKey, ct);
                if (unrealPak.Entries.Count > 0)
                {
                    return unrealPak;
                }
            }
            catch (ArgumentException)
            {
                throw; // Geçersiz anahtar hatasını doğrudan fırlat
            }
            catch
            {
                // CUE4Parse açamazsa klasik/yerel Unreal ayrıştırıcısına (Adım 7) devam et
            }
        }

        // 7. Klasik Unreal Engine Fallback (Legacy V1-V7 & Yerel V8-V11)
        byte[]? keyBytes = null;
        if (!string.IsNullOrWhiteSpace(aesKey))
        {
            if (!AesDecryptionHelper.ValidateKey(aesKey, out keyBytes))
                throw new ArgumentException("Geçersiz AES anahtarı. 64 hex karakter (256-bit) bekleniyor.");
        }

        var footer = await ReadUnrealFooterAsync(fs, ct);
        if (footer != null)
        {
            var entries = await ReadUnrealIndexAsync(fs, footer, keyBytes, ct);
            if (entries.Count > 0)
            {
                return new PakArchive
                {
                    FilePath = filePath,
                    Version = footer.Version,
                    IsEncrypted = footer.IsEncrypted,
                    AesKey = aesKey,
                    MountPoint = footer.MountPoint,
                    Format = PakFormat.Unreal,
                    Entries = entries
                };
            }
        }

        // 8. Heuristic İçerik Kurtarma Modu (Özel / Bilinmeyen tüm .pak motorları için güvenlik ağı)
        var heuristicEntries = await HeuristicScanAsync(fs, ct);
        if (heuristicEntries.Count > 0)
        {
            return new PakArchive
            {
                FilePath = filePath,
                Version = 0,
                IsEncrypted = false,
                MountPoint = "Recovered",
                Format = PakFormat.Heuristic,
                Entries = heuristicEntries
            };
        }

        throw new InvalidDataException("Bu .pak dosyası geçerli bir Unreal Engine, Larian LSPak, CryEngine, Godot, Unity veya bilinen arşiv formatında değil.");
    }

    /// <inheritdoc/>
    public async Task<Stream> ExtractEntryAsync(
        PakArchive archive,
        PakEntry entry,
        CancellationToken ct = default)
    {
        // Larian LSPak
        if (archive.Format == PakFormat.Larian)
        {
            return await LSPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // ZIP tabanlı
        if (archive.Format == PakFormat.Zip)
        {
            return await ZipPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // Godot Engine
        if (archive.Format == PakFormat.Godot)
        {
            return await GodotPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // Unity Engine
        if (archive.Format == PakFormat.Unity)
        {
            return await UnityPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // Classic PACK
        if (archive.Format == PakFormat.Classic)
        {
            return await ClassicPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // Unreal Engine (Modern CUE4Parse önbellek veya çıkarma)
        if (archive.Format == PakFormat.Unreal)
        {
            try
            {
                return await UnrealCuePakHandler.ExtractEntryAsync(archive.FilePath, entry, archive.AesKey, ct);
            }
            catch (Exception ex) when (entry.DataOffset <= 0)
            {
                // CUE4Parse girdileri DataOffset=0 ile listelenir; ofset tabanlı yedek okuma anlamsızdır (ilk baytları 'dosya' sanıp çöp döner).
                throw new InvalidOperationException(
                    $"'{entry.VirtualPath}' çıkarılamadı: {ex.Message}" +
                    (UnrealCuePakHandler.OodleStatus is { Length: > 0 } st ? $" (Oodle: {st})" : ""), ex);
            }
            catch
            {
                // Yerel ayrıştırıcıyla açılmış (gerçek ofsetli) girdiler için ofset tabanlı okuma sürer
            }
        }

        // Standart / Klasik ofset tabanlı çıkarma
        await using var fs = new FileStream(archive.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (entry.DataOffset < 0 || entry.CompressedSize < 0 || entry.CompressedSize > MaxEntrySize ||
            entry.DataOffset + entry.CompressedSize > fs.Length)
            throw new InvalidDataException($"Geçersiz girdi ofseti/boyutu: {entry.VirtualPath}");

        fs.Seek(entry.DataOffset, SeekOrigin.Begin);
        var compressedData = new byte[entry.CompressedSize];
        await fs.ReadExactlyAsync(compressedData, ct);

        return await DecompressAsync(compressedData, entry, ct);
    }

    /// <inheritdoc/>
    public async Task WriteAsync(
        string outputPath,
        string mountPoint,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        CancellationToken ct = default,
        PakFormat sourceFormat = PakFormat.Unknown,
        string? aesKey = null,
        string? sourceArchivePath = null)
    {
        // 1. Larian LSPak formatı (DOS2 / Baldur's Gate 3)
        if (sourceFormat == PakFormat.Larian)
        {
            await LSPakHandler.WriteArchiveAsync(outputPath, files, ct);
            return;
        }

        // 2. CryEngine / Lumberyard / ZIP tabanlı .pak
        if (sourceFormat == PakFormat.Zip)
        {
            await ZipPakHandler.WriteArchiveAsync(outputPath, files, ct);
            return;
        }

        // 3. Godot Engine .pck / .pak
        if (sourceFormat == PakFormat.Godot)
        {
            await GodotPakHandler.WriteArchiveAsync(outputPath, files, ct);
            return;
        }

        // 4. Classic ID Tech / Quake / GoldSrc PACK
        if (sourceFormat == PakFormat.Classic)
        {
            await ClassicPakHandler.WriteArchiveAsync(outputPath, files, ct);
            return;
        }

        // 5. Unity Engine AssetBundle .pak
        if (sourceFormat == PakFormat.Unity)
        {
            // Unity dosyaları sıfırdan üretilemez: özgün dosya yamalanır (yalnızca değişen nesneler)
            if (string.IsNullOrEmpty(sourceArchivePath) || !File.Exists(sourceArchivePath))
                throw new InvalidOperationException("Unity dosyası yazmak için özgün dosyanın yolu gerekli.");
            await UnityPakHandler.WriteArchiveAsync(sourceArchivePath, outputPath, files, ct);
            return;
        }

        // 6. Unreal Engine .pak (V8 standart yama paketi formatı)
        byte[]? keyBytes = null;
        if (!string.IsNullOrWhiteSpace(aesKey) && AesDecryptionHelper.ValidateKey(aesKey, out var parsedKey))
        {
            keyBytes = parsedKey;
        }

        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        UnrealPakWriter.Write(fs, mountPoint, files, keyBytes);
        await fs.FlushAsync(ct);
    }

    /// <summary>
    /// IoStore kullanan oyunlar için <paramref name="pakPath"/> yamasının yanına aynı adlı <c>.utoc</c>/<c>.ucas</c>
    /// eşini yazar (paket içermeyen, yalnızca konteyner başlığı olan konteyner). Motor bir .pak'ı bağlarken aynı adlı
    /// .utoc'u da bağlar; eş, kaynak oyunun TOC ve konteyner başlığı sürümleriyle üretilir.
    /// </summary>
    public static Task WriteIoStoreCompanionAsync(string pakPath, PakArchive source, CancellationToken ct = default)
    {
        if (!source.IsIoStore)
            throw new InvalidOperationException("Kaynak arşiv bir IoStore konteyneri değil.");

        return Task.Run(() =>
        {
            var basePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pakPath)) ?? ".", Path.GetFileNameWithoutExtension(pakPath));
            var mountPoint = string.IsNullOrWhiteSpace(source.MountPoint) ? "../../../" : source.MountPoint;
            IoStoreContainerWriter.Write(
                basePath,
                [],
                (CUE4Parse.UE4.IO.Objects.EIoStoreTocVersion)source.IoStoreTocVersion,
                (CUE4Parse.UE4.IO.Objects.EIoContainerHeaderVersion)source.IoStoreContainerHeaderVersion,
                mountPoint);
        }, ct);
    }

    // ─── Unreal Engine Archive Helpers ────────────────────────────────────────

    private static async Task<PakFooter?> ReadUnrealFooterAsync(FileStream fs, CancellationToken ct)
    {
        var fileSize = fs.Length;
        if (fileSize < 44) return null;

        var scanSize = Math.Min(fileSize, 8192);
        fs.Seek(-scanSize, SeekOrigin.End);
        var scanBuf = new byte[scanSize];
        await fs.ReadExactlyAsync(scanBuf, ct);

        int magicPos = -1;
        for (int i = (int)scanSize - 4; i >= 0; i--)
        {
            uint candidate = BitConverter.ToUInt32(scanBuf, i);
            if (candidate == PakMagic) { magicPos = i; break; }
        }

        if (magicPos < 0) return null;

        using var br = new BinaryReader(new MemoryStream(scanBuf));
        br.BaseStream.Position = magicPos;

        uint magic = br.ReadUInt32();
        int version = br.ReadInt32();
        long indexOffset = br.ReadInt64();
        long indexSize = br.ReadInt64();
        br.ReadBytes(20); // SHA1 hash

        bool isEncrypted = false;
        if (br.BaseStream.Position < br.BaseStream.Length)
            isEncrypted = br.ReadByte() != 0;

        return new PakFooter(version, indexOffset, indexSize, isEncrypted, string.Empty);
    }

    private static async Task<IReadOnlyList<PakEntry>> ReadUnrealIndexAsync(
        FileStream fs, PakFooter footer, byte[]? keyBytes, CancellationToken ct)
    {
        if (footer.IndexOffset <= 0 || footer.IndexSize <= 0 || footer.IndexOffset >= fs.Length ||
            footer.IndexSize > MaxIndexSize || footer.IndexOffset + footer.IndexSize > fs.Length)
            return [];

        fs.Seek(footer.IndexOffset, SeekOrigin.Begin);
        var indexData = new byte[footer.IndexSize];
        await fs.ReadExactlyAsync(indexData, ct);

        if (footer.IsEncrypted)
        {
            if (keyBytes is null)
                throw new InvalidOperationException("Arşiv şifreli ancak AES anahtarı girilmedi.");
            indexData = AesDecryptionHelper.Decrypt(indexData, keyBytes);
        }

        using var br = new BinaryReader(new MemoryStream(indexData), System.Text.Encoding.UTF8);

        int mountLen = br.ReadInt32();
        var mountPoint = (mountLen > 0 && mountLen < 1024)
            ? new string(br.ReadChars(mountLen)).TrimEnd('\0')
            : string.Empty;

        int entryCount = br.ReadInt32();
        if (entryCount < 0 || entryCount > 500000) return [];

        var entries = new List<PakEntry>(entryCount);

        for (int i = 0; i < entryCount && br.BaseStream.Position < br.BaseStream.Length; i++)
        {
            int pathLen = br.ReadInt32();
            string path = (pathLen > 0 && pathLen < 4096)
                ? new string(br.ReadChars(pathLen)).TrimEnd('\0')
                : $"file_{i}";

            var entry = new PakEntry
            {
                VirtualPath = path.Replace('\\', '/'),
                DataOffset = br.ReadInt64(),
                CompressedSize = br.ReadInt64(),
                UncompressedSize = br.ReadInt64(),
                Compression = (CompressionMethod)br.ReadInt32()
            };

            entry.Hash = br.ReadBytes(20);

            if (footer.Version >= 4 && br.BaseStream.Position < br.BaseStream.Length)
            {
                entry.IsEncrypted = br.ReadByte() != 0;
                br.ReadUInt32(); // BlockSize
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static Task<Stream> DecompressAsync(byte[] data, PakEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (entry.UncompressedSize < 0 || entry.UncompressedSize > MaxEntrySize)
            throw new InvalidDataException($"Açılmış boyut sınır dışı: {entry.VirtualPath} ({entry.UncompressedSize} bayt)");
        Stream result = entry.Compression switch
        {
            CompressionMethod.None => new MemoryStream(data),
            CompressionMethod.Zlib or CompressionMethod.Gzip => DecompressZlib(data, entry.UncompressedSize),
            CompressionMethod.LZ4 => DecompressLZ4(data, entry.UncompressedSize),
            CompressionMethod.Zstd => DecompressZstd(data, entry.UncompressedSize),
            CompressionMethod.Oodle => DecompressOodle(data, entry.UncompressedSize),
            _ => new MemoryStream(data)
        };
        return Task.FromResult(result);
    }

    private static MemoryStream DecompressZlib(byte[] data, long uncompressedSize)
    {
        try
        {
            int skip = (data.Length > 2 && data[0] == 0x78) ? 2 : 0;
            int trim = skip == 2 ? 4 : 0;
            using var ms = new MemoryStream(data, skip, data.Length - skip - trim);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            var output = new MemoryStream((int)Math.Max(64, uncompressedSize));
            SafeIo.CopyBounded(deflate, output, SafeIo.DecompressionLimit(uncompressedSize));
            output.Position = 0;
            return output;
        }
        catch (DecompressionLimitExceededException)
        {
            throw; // Sıkıştırma bombası: ikinci yöntemle yeniden açmayı deneme
        }
        catch
        {
            using var ms = new MemoryStream(data);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            var output = new MemoryStream((int)Math.Max(64, uncompressedSize));
            SafeIo.CopyBounded(deflate, output, SafeIo.DecompressionLimit(uncompressedSize));
            output.Position = 0;
            return output;
        }
    }

    private static MemoryStream DecompressLZ4(byte[] data, long uncompressedSize)
    {
        var output = new byte[Math.Max(64, uncompressedSize)];
        int decoded = LZ4Codec.Decode(data, 0, data.Length, output, 0, output.Length);
        return new MemoryStream(output, 0, decoded > 0 ? decoded : output.Length);
    }

    private static MemoryStream DecompressZstd(byte[] data, long uncompressedSize)
    {
        using var decompressor = new Decompressor();
        var output = decompressor.Unwrap(data, (int)Math.Max(64, uncompressedSize)).ToArray();
        return new MemoryStream(output);
    }

    private static MemoryStream DecompressOodle(byte[] data, long uncompressedSize)
    {
        var decompressed = OodleHelper.Decompress(data, uncompressedSize);
        return new MemoryStream(decompressed ?? data);
    }



    // ─── 5. Heuristic İçerik Kurtarıcısı ──────────────────────────────────────

    private static async Task<List<PakEntry>> HeuristicScanAsync(FileStream fs, CancellationToken ct)
    {
        var entries = new List<PakEntry>();
        long fileSize = fs.Length;
        if (fileSize < 64) return entries;

        fs.Seek(0, SeekOrigin.Begin);
        byte[] buffer = new byte[Math.Min(fileSize, 1024 * 1024 * 16)]; // ilk 16 MB tara
        int bytesRead = await fs.ReadAsync(buffer, ct);

        // 1. Locres Magic Arama: 0x0E, 0x14, 0x74, 0x75
        for (int i = 0; i < bytesRead - 16; i++)
        {
            if (buffer[i] == 0x0E && buffer[i + 1] == 0x14 && buffer[i + 2] == 0x74 && buffer[i + 3] == 0x75)
            {
                entries.Add(new PakEntry
                {
                    VirtualPath = $"Recovered/Localization_{entries.Count + 1}.locres",
                    DataOffset = i,
                    UncompressedSize = Math.Min(5000000, fileSize - i),
                    CompressedSize = Math.Min(5000000, fileSize - i),
                    Compression = CompressionMethod.None
                });
            }
        }

        // 2. XML / LSX / Text Arama
        for (int i = 0; i < bytesRead - 32; i++)
        {
            if (buffer[i] == '<' && buffer[i + 1] == '?' && buffer[i + 2] == 'x' && buffer[i + 3] == 'm')
            {
                entries.Add(new PakEntry
                {
                    VirtualPath = $"Recovered/Dialogs_{entries.Count + 1}.xml",
                    DataOffset = i,
                    UncompressedSize = Math.Min(2000000, fileSize - i),
                    CompressedSize = Math.Min(2000000, fileSize - i),
                    Compression = CompressionMethod.None
                });
            }
        }

        return entries;
    }
}
