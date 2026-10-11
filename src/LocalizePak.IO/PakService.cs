using System.IO.Compression;
using K4os.Compression.LZ4;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats;
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

        // 1. Larian LSPak format kontrolü (Magic: "LSPK" = 0x4B50534C)
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
                // Bilinmeyen LSPak varyantı durumunda diğer ayrıştırıcılara veya Heuristic moda devam et
            }
        }

        // 2. CryEngine / Lumberyard / ZIP tabanlı .pak (Magic: "PK\x03\x04")
        if (ZipPakHandler.IsZipPak(fs))
        {
            var zipPak = await ZipPakHandler.ReadArchiveAsync(filePath, ct);
            zipPak.Format = PakFormat.Zip;
            return zipPak;
        }

        // 3. Classic ID Tech / Quake / Valve PACK (Magic: "PACK")
        if (ClassicPakHandler.IsClassicPak(fs))
        {
            var classicPak = await ClassicPakHandler.ReadArchiveAsync(filePath, ct);
            classicPak.Format = PakFormat.Classic;
            return classicPak;
        }

        // 4. Unreal Engine .pak (Magic: 0x5A6F12E1 footer taraması)
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
                    MountPoint = footer.MountPoint,
                    Format = PakFormat.Unreal,
                    Entries = entries
                };
            }
        }

        // 5. Heuristic İçerik Kurtarma Modu (Bilinmeyen veya Özel .pak Arşivleri)
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

        throw new InvalidDataException("Bu .pak dosyası geçerli bir Unreal Engine, Larian LSPak veya bilinen arşiv formatında değil.");
    }

    /// <inheritdoc/>
    public async Task<Stream> ExtractEntryAsync(
        PakArchive archive,
        PakEntry entry,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(archive.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

        // Larian LSPak
        if (LSPakHandler.IsLSPak(fs))
        {
            return await LSPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // ZIP tabanlı
        if (ZipPakHandler.IsZipPak(fs))
        {
            return await ZipPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // Classic PACK
        if (ClassicPakHandler.IsClassicPak(fs))
        {
            return await ClassicPakHandler.ExtractEntryAsync(archive.FilePath, entry, ct);
        }

        // Unreal Engine / Genel format
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
        PakFormat sourceFormat = PakFormat.Unknown)
    {
        // ✅ KRİTİK: Orijinal arşivin formatına göre doğru serileştiriciyi kullan.
        // Eskiden sadece dosya uzantısına bakılıyordu — bu Larian arşivlerini
        // Unreal formatında yazmasına ve oyunun çökmesine yol açıyordu.
        if (sourceFormat == PakFormat.Larian)
        {
            await LSPakHandler.WriteArchiveAsync(outputPath, files, ct);
            return;
        }

        if (sourceFormat == PakFormat.Zip)
        {
            await ZipPakHandler.WriteArchiveAsync(outputPath, files, ct);
            return;
        }

        // Unreal Engine .pak yaz (sourceFormat == Unreal, Classic, Heuristic veya Unknown)
        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        await using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true);

        var entries = new List<(PakEntry Entry, byte[] CompressedData)>();

        foreach (var (virtualPath, data) in files)
        {
            ct.ThrowIfCancellationRequested();
            var compressed = CompressZlib(data);
            var entry = new PakEntry
            {
                VirtualPath = virtualPath,
                DataOffset = fs.Position,
                UncompressedSize = data.Length,
                CompressedSize = compressed.Length,
                Compression = CompressionMethod.Zlib
            };
            await fs.WriteAsync(compressed, ct);
            entries.Add((entry, compressed));
        }

        long indexOffset = fs.Position;
        WriteIndex(writer, mountPoint, entries.Select(e => e.Entry).ToList());
        WriteFooter(writer, indexOffset, fs.Position - indexOffset);
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
            deflate.CopyTo(output);
            output.Position = 0;
            return output;
        }
        catch
        {
            using var ms = new MemoryStream(data);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            var output = new MemoryStream((int)Math.Max(64, uncompressedSize));
            deflate.CopyTo(output);
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

    private static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return output.ToArray();
    }

    private static void WriteIndex(BinaryWriter writer, string mountPoint, IReadOnlyList<PakEntry> entries)
    {
        writer.Write(mountPoint.Length + 1);
        writer.Write(System.Text.Encoding.UTF8.GetBytes(mountPoint + "\0"));
        writer.Write(entries.Count);

        foreach (var entry in entries)
        {
            writer.Write(entry.VirtualPath.Length + 1);
            writer.Write(System.Text.Encoding.UTF8.GetBytes(entry.VirtualPath + "\0"));
            writer.Write(entry.DataOffset);
            writer.Write(entry.CompressedSize);
            writer.Write(entry.UncompressedSize);
            writer.Write((int)entry.Compression);
            writer.Write(entry.Hash ?? new byte[20]);
            writer.Write((byte)0); // isEncrypted
            writer.Write((uint)0x10000); // blockSize
        }
    }

    private static void WriteFooter(BinaryWriter writer, long indexOffset, long indexSize)
    {
        writer.Write(PakMagic);
        writer.Write((int)8); // Version 8
        writer.Write(indexOffset);
        writer.Write(indexSize);
        writer.Write(new byte[20]); // Hash
        writer.Write((byte)0); // isEncrypted
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
