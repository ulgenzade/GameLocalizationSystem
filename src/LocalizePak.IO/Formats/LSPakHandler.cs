using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO.Helpers;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Larian Studios (Divinity: Original Sin 1 &amp; 2, Baldur's Gate 3) LSPak binary format işleyicisi (V7–V18).
/// Magic: "LSPK" (0x4B50534C)
/// </summary>
public sealed class LSPakHandler
{
    public const uint LSPKMagic = 0x4B50534C;

    /// <summary>
    /// Dosyanın LSPak formatında olup olmadığını kontrol eder.
    /// Hem baştaki (BG3/DOS1) hem de sondaki Trailer (DOS2 Definitive Edition V13) imzalarını tanır.
    /// </summary>
    public static bool IsLSPak(Stream stream)
    {
        if (stream.Length < 8) return false;
        var pos = stream.Position;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        
        // 1. Başta "LSPK" kontrolü (Header-first: BG3, DOS1)
        stream.Seek(0, SeekOrigin.Begin);
        uint magicHeader = reader.ReadUInt32();
        if (magicHeader == LSPKMagic)
        {
            stream.Seek(pos, SeekOrigin.Begin);
            return true;
        }

        // 2. Sonda "LSPK" kontrolü (Trailer-based: DOS2 Definitive Edition V13)
        if (stream.Length >= 40)
        {
            stream.Seek(-4, SeekOrigin.End);
            uint magicTrailer = reader.ReadUInt32();
            if (magicTrailer == LSPKMagic)
            {
                stream.Seek(pos, SeekOrigin.Begin);
                return true;
            }
        }

        stream.Seek(pos, SeekOrigin.Begin);
        return false;
    }

    /// <summary>
    /// LSPak arşivini açar ve içindeki dosyaları listeler.
    /// </summary>
    public static async Task<PakArchive> ReadArchiveAsync(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

        uint magic = 0;
        uint version = 13;
        long fileListOffset = 0;
        uint fileListSize = 0;
        byte flags = 0;
        byte priority = 0;
        byte[] md5 = new byte[16];
        ushort numParts = 0;

        fs.Seek(0, SeekOrigin.Begin);
        magic = reader.ReadUInt32();

        if (magic == LSPKMagic)
        {
            // Header başta
            version = reader.ReadUInt32();
            if (version >= 13)
            {
                fileListOffset = (long)reader.ReadUInt64();
                fileListSize = reader.ReadUInt32();
                flags = reader.ReadByte();
                priority = reader.ReadByte();
                md5 = reader.ReadBytes(16);
                numParts = reader.ReadUInt16();
            }
            else if (version == 10)
            {
                uint dataOffset = reader.ReadUInt32();
                fileListSize = reader.ReadUInt32();
                numParts = reader.ReadUInt16();
                flags = reader.ReadByte();
                priority = reader.ReadByte();
                md5 = reader.ReadBytes(16);
                uint numFiles = reader.ReadUInt32();
                fileListOffset = 36;
            }
            else
            {
                uint dataOffset = reader.ReadUInt32();
                numParts = (ushort)reader.ReadUInt32();
                fileListSize = reader.ReadUInt32();
                byte littleEndian = reader.ReadByte();
                uint numFiles = reader.ReadUInt32();
                fileListOffset = 21;
            }
        }
        else if (fs.Length >= 40)
        {
            // Trailer sonda (DOS2 Definitive Edition V13)
            fs.Seek(-4, SeekOrigin.End);
            magic = reader.ReadUInt32();
            if (magic != LSPKMagic)
                throw new InvalidDataException("Geçerli bir Larian LSPak dosyası değil.");

            fs.Seek(-40, SeekOrigin.End);
            version = reader.ReadUInt32();
            fileListOffset = (long)reader.ReadUInt32();
            fileListSize = reader.ReadUInt32();
            flags = reader.ReadByte();
            priority = reader.ReadByte();
            md5 = reader.ReadBytes(16);
            numParts = reader.ReadUInt16();
        }
        else
        {
            throw new InvalidDataException("Geçerli bir Larian LSPak dosyası değil.");
        }

        // Dosya listesine (Index) git
        if (fileListOffset <= 0 || fileListOffset >= fs.Length)
            throw new InvalidDataException($"LSPak dosya listesi ofseti geçersiz: {fileListOffset}");

        if (fileListSize == 0 || fileListSize > 256u * 1024 * 1024 || fileListOffset + fileListSize > fs.Length)
            throw new InvalidDataException($"LSPak dosya listesi boyutu geçersiz: {fileListSize}");

        fs.Seek(fileListOffset, SeekOrigin.Begin);
        byte[] indexData = new byte[fileListSize];
        await fs.ReadExactlyAsync(indexData, ct);

        // Sıkıştırma kontrolü (flags & 0x0F: 0=None, 1=Zlib, 2=LZ4)
        byte compressionMethod = (byte)(flags & 0x0F);
        if (compressionMethod != 0)
        {
            indexData = DecompressIndex(indexData, compressionMethod);
        }

        var entries = ParseFileList(indexData, version);

        // Girdilerin geçerliliğini doğrula
        bool allValid = entries.Count > 0 && entries.All(e =>
            e.DataOffset >= 0 &&
            (e.DataOffset + e.CompressedSize <= fs.Length || e.CompressedSize <= fs.Length) &&
            e.UncompressedSize < 500_000_000);

        if (!allValid)
        {
            throw new InvalidDataException("LSPak dosya tablosu girdileri geçersiz veya desteklenmeyen bir varyant.");
        }

        return new PakArchive
        {
            FilePath = filePath,
            Version = (int)version,
            MountPoint = "Localization",
            IsEncrypted = false,
            Entries = entries
        };
    }

    /// <summary>
    /// LSPak içerisinden belirtilen girdiyi ayıklar.
    /// </summary>
    public static async Task<Stream> ExtractEntryAsync(string pakFilePath, PakEntry entry, CancellationToken ct = default)
    {
        await using var fs = new FileStream(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

        // Standart dosya formatı
        if (entry.DataOffset < 0 || entry.DataOffset >= fs.Length ||
            entry.CompressedSize <= 0 || entry.CompressedSize > SafeIo.MaxEntrySize ||
            entry.DataOffset + entry.CompressedSize > fs.Length)
        {
            return new MemoryStream();
        }

        fs.Seek(entry.DataOffset, SeekOrigin.Begin);

        var data = new byte[entry.CompressedSize];
        await fs.ReadExactlyAsync(data, ct);

        if (entry.Compression == CompressionMethod.None || entry.CompressedSize == entry.UncompressedSize)
        {
            return new MemoryStream(data);
        }

        if (entry.Compression == CompressionMethod.LZ4)
        {
            try
            {
                int targetSize = (int)Math.Clamp(entry.UncompressedSize, 1, 50_000_000);
                var target = new byte[targetSize];
                int decoded = LZ4Codec.Decode(data, 0, data.Length, target, 0, target.Length);
                if (decoded > 0) return new MemoryStream(target, 0, decoded);
            }
            catch { }

            // Fallback Zlib
            var zlibFallback = SafeDecompressZlib(data, entry.UncompressedSize);
            if (zlibFallback.Length > 0 && zlibFallback.Length != data.Length)
                return zlibFallback;
        }

        if (entry.Compression == CompressionMethod.Zlib)
        {
            var zlibStream = SafeDecompressZlib(data, entry.UncompressedSize);
            if (zlibStream.Length > 0 && zlibStream.Length != data.Length)
                return zlibStream;

            // Fallback LZ4
            try
            {
                int targetSize = (int)Math.Clamp(entry.UncompressedSize, 1, 50_000_000);
                var target = new byte[targetSize];
                int decoded = LZ4Codec.Decode(data, 0, data.Length, target, 0, target.Length);
                if (decoded > 0) return new MemoryStream(target, 0, decoded);
            }
            catch { }
        }

        return new MemoryStream(data);
    }

    private static MemoryStream SafeDecompressZlib(byte[] data, long uncompressedSize)
    {
        int initialCap = (int)Math.Clamp(uncompressedSize, 64, 50_000_000);
        long limit = SafeIo.DecompressionLimit(uncompressedSize);

        // 1. Standart ZLibStream (.NET 8 yerel RFC 1950)
        try
        {
            using var ms = new MemoryStream(data);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            var outMs = new MemoryStream(initialCap);
            SafeIo.CopyBounded(zlib, outMs, limit);
            outMs.Seek(0, SeekOrigin.Begin);
            if (outMs.Length > 0) return outMs;
        }
        catch { }

        // 2. Raw DeflateStream
        try
        {
            int skip = (data.Length > 2 && data[0] == 0x78) ? 2 : 0;
            int endTrim = skip == 2 ? 4 : 0;
            using var ms = new MemoryStream(data, skip, data.Length - skip - endTrim);
            using var def = new DeflateStream(ms, CompressionMode.Decompress);
            var outMs = new MemoryStream(initialCap);
            SafeIo.CopyBounded(def, outMs, limit);
            outMs.Seek(0, SeekOrigin.Begin);
            if (outMs.Length > 0) return outMs;
        }
        catch { }

        return new MemoryStream(data);
    }

    /// <summary>
    /// Yeni bir LSPak arşivi oluşturur veya mevcut dosyaları paketler.
    /// DOS2 Definitive Edition (V13) için 280 byte struct + Trailer, BG3 için 292 byte struct standardını uygular.
    /// </summary>
    public static async Task WriteArchiveAsync(
        string outputPath,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        CancellationToken ct = default,
        uint version = 13)
    {
        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        await using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

        // Header yeri ayırt (40 byte - V13/V15/V16 standardı)
        byte[] headerPlaceholder = new byte[40];
        await fs.WriteAsync(headerPlaceholder, ct);

        var entries = new List<PakEntry>();

        // Dosya verilerini yaz
        foreach (var (virtualPath, data) in files)
        {
            ct.ThrowIfCancellationRequested();
            long offset = fs.Position;

            // Zlib sıkıştırma uygula
            var compressed = CompressZlib(data);
            await fs.WriteAsync(compressed, ct);

            entries.Add(new PakEntry
            {
                VirtualPath = virtualPath,
                DataOffset = offset,
                CompressedSize = compressed.Length,
                UncompressedSize = data.Length,
                Compression = CompressionMethod.Zlib
            });
        }

        long fileListOffset = fs.Position;

        // Dosya listesini serileştir
        using var indexMs = new MemoryStream();
        using var indexWriter = new BinaryWriter(indexMs, Encoding.UTF8);

        indexWriter.Write((uint)entries.Count);
        foreach (var e in entries)
        {
            byte[] nameBuf = new byte[256];
            var pathBytes = Encoding.UTF8.GetBytes(e.VirtualPath);
            Array.Copy(pathBytes, nameBuf, Math.Min(pathBytes.Length, 255));
            indexWriter.Write(nameBuf);

            if (version <= 13)
            {
                // V13 (DOS2 Definitive Edition): 280 byte sabit alan (Name: 256, Offset: 4, Comp: 4, Uncomp: 4, Part: 4, Flags: 4, Crc: 4)
                indexWriter.Write((uint)e.DataOffset);
                indexWriter.Write((uint)e.CompressedSize);
                indexWriter.Write((uint)e.UncompressedSize);
                indexWriter.Write((uint)0); // archivePart
                indexWriter.Write((uint)1); // flags (1 = Zlib)
                indexWriter.Write((uint)0); // crc
            }
            else
            {
                // V15/V16/V18 (BG3 / Standart): 292 byte sabit alan (Name: 256, Offset: 8, Comp: 8, Uncomp: 8, Part: 4, Flags: 4, Crc: 4)
                indexWriter.Write((ulong)e.DataOffset);
                indexWriter.Write((ulong)e.CompressedSize);
                indexWriter.Write((ulong)e.UncompressedSize);
                indexWriter.Write((uint)0); // archivePart
                indexWriter.Write((uint)1); // flags (1 = Zlib)
                indexWriter.Write((uint)0); // crc
            }
        }

        byte[] rawIndex = indexMs.ToArray();
        await fs.WriteAsync(rawIndex, ct);
        uint fileListSize = (uint)rawIndex.Length;

        // Başa dön ve header'ı tamamla (BG3 / DOS1 uyumu)
        fs.Seek(0, SeekOrigin.Begin);
        writer.Write(LSPKMagic);
        writer.Write(version); // Version (13 = DOS2 DE, 15/18 = BG3)
        writer.Write((ulong)fileListOffset);
        writer.Write(fileListSize);
        writer.Write((byte)0); // Flags
        writer.Write((byte)0); // Priority
        writer.Write(new byte[16]); // MD5 placeholder
        writer.Write((ushort)0); // num parts

        // Sona dön ve Trailer'ı ekle (DOS2 Definitive Edition V13 uyumu)
        fs.Seek(0, SeekOrigin.End);
        writer.Write(version); // Version
        writer.Write((uint)fileListOffset); // Offset
        writer.Write(fileListSize); // Size
        writer.Write((byte)0); // Flags
        writer.Write((byte)0); // Priority
        writer.Write(new byte[16]); // MD5 placeholder
        writer.Write((ushort)0); // num parts
        writer.Write((uint)40); // Trailer Size (40 bytes)
        writer.Write(LSPKMagic); // "LSPK" magic at the very end
    }

    public static List<PakEntry> ParseFileList(byte[] indexData, uint version)
    {
        var entries = new List<PakEntry>();
        using var ms = new MemoryStream(indexData);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        if (ms.Length < 4) return entries;
        uint count = reader.ReadUInt32();

        // Aşırı büyük count koruması
        if (count > 500000) return entries;

        long remaining = ms.Length - ms.Position;
        int entrySize = count > 0 ? (int)(remaining / count) : 0;

        // V13 (DOS2 Definitive Edition): 280 byte sabit alan (Name: 256, Part: 4, Offset: 4, Comp: 4, Uncomp: 4, Flags: 4, Crc: 4)
        bool isV13_280 = entrySize == 280 || (remaining == count * 280);

        // V15/V16/V18 (BG3 / Standart): 292/296 byte sabit alan (Name: 256, Offset: 8, Comp: 8, Uncomp: 8, Part: 4, Flags: 4, Crc: 4)
        bool isV15_292 = entrySize >= 292 || (remaining >= count * 292);

        for (uint i = 0; i < count && ms.Position < ms.Length; i++)
        {
            string path = string.Empty;
            long offset = 0;
            long compSize = 0;
            long uncompSize = 0;
            byte compMethod = 0;
            bool isSolid = false;

            try
            {
                if (isV13_280)
                {
                    byte[] nameBytes = reader.ReadBytes(256);
                    int nullIdx = Array.IndexOf<byte>(nameBytes, 0);
                    int len = nullIdx >= 0 ? nullIdx : nameBytes.Length;
                    path = len > 0 ? Encoding.UTF8.GetString(nameBytes, 0, len) : $"file_{i}";

                    offset = reader.ReadUInt32(); // 256..259: OffsetInFile
                    compSize = reader.ReadUInt32(); // 260..263: SizeOnDisk
                    uncompSize = reader.ReadUInt32(); // 264..267: UncompressedSize
                    reader.ReadUInt32(); // 268..271: archivePart
                    uint flags = reader.ReadUInt32(); // 272..275: Flags
                    reader.ReadUInt32(); // 276..279: crc

                    compMethod = (byte)(flags & 0x0F);
                    isSolid = false;
                }
                else if (isV15_292)
                {
                    byte[] nameBytes = reader.ReadBytes(256);
                    int nullIdx = Array.IndexOf<byte>(nameBytes, 0);
                    int len = nullIdx >= 0 ? nullIdx : nameBytes.Length;
                    path = len > 0 ? Encoding.UTF8.GetString(nameBytes, 0, len) : $"file_{i}";

                    offset = (long)reader.ReadUInt64(); // 256..263: OffsetInFile
                    compSize = (long)reader.ReadUInt64(); // 264..271: SizeOnDisk
                    uncompSize = (long)reader.ReadUInt64(); // 272..279: UncompressedSize
                    reader.ReadUInt32(); // 280..283: archivePart
                    uint flags = reader.ReadUInt32(); // 284..287: Flags
                    reader.ReadUInt32(); // 288..291: crc
                    if (version >= 16 && ms.Position + 4 <= ms.Length) reader.ReadUInt32();

                    compMethod = (byte)(flags & 0x0F);
                    isSolid = false;
                }
                else
                {
                    // Dinamik fallback
                    int len = reader.ReadInt32();
                    path = len > 0 && ms.Position + len <= ms.Length ? Encoding.UTF8.GetString(reader.ReadBytes(len)) : $"file_{i}";
                    offset = (long)reader.ReadUInt64();
                    compSize = (long)reader.ReadUInt64();
                    uncompSize = (long)reader.ReadUInt64();
                    reader.ReadUInt32();
                    uint flags = reader.ReadUInt32();
                    compMethod = (byte)(flags & 0x0F);
                    isSolid = false;
                }
            }
            catch (EndOfStreamException)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                entries.Add(new PakEntry
                {
                    VirtualPath = path.Replace('\\', '/'),
                    DataOffset = offset,
                    CompressedSize = compSize,
                    UncompressedSize = uncompSize,
                    IsSolid = isSolid,
                    // SolidStreamSize ReadArchiveAsync tarafından sonradan doldurulur
                    Compression = compMethod switch
                    {
                        1 => CompressionMethod.Zlib,
                        2 => CompressionMethod.LZ4,
                        _ => CompressionMethod.None
                    }
                });
            }
        }

        return entries;
    }

    public static byte[] DecompressIndex(byte[] data, byte method)
    {
        // 1. Larian V13/V15/V16 LZ4 Index Formatı (İlk 4 byte numFiles, devamı LZ4 sıkıştırmalı 280/292 byte tablo)
        if (data.Length > 4)
        {
            uint count = BitConverter.ToUInt32(data, 0);
            if (count > 0 && count < 500000)
            {
                try
                {
                    int expectedSize = (int)count * 292;
                    byte[] lz4Target = new byte[expectedSize + 65536];
                    int decoded = LZ4Codec.Decode(data, 4, data.Length - 4, lz4Target, 0, lz4Target.Length);
                    if (decoded > 0)
                    {
                        using var outStream = new MemoryStream(decoded + 4);
                        using var writer = new BinaryWriter(outStream);
                        writer.Write(count);
                        writer.Write(lz4Target, 0, decoded);
                        return outStream.ToArray();
                    }
                }
                catch { }
            }
        }

        // 2. Doğrudan LZ4 açma
        try
        {
            byte[] lz4Target = new byte[Math.Max(64, data.Length * 15)];
            int decoded = LZ4Codec.Decode(data, 0, data.Length, lz4Target, 0, lz4Target.Length);
            if (decoded > 0)
            {
                return lz4Target.Take(decoded).ToArray();
            }
        }
        catch { }

        // 3. Standart ZLib
        try
        {
            using var ms = new MemoryStream(data);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            zlib.CopyTo(outMs);
            var res = outMs.ToArray();
            if (res.Length > 0) return res;
        }
        catch { }

        // 4. Raw Deflate
        try
        {
            int skip = (data.Length > 2 && data[0] == 0x78) ? 2 : 0;
            int endTrim = skip == 2 ? 4 : 0;
            using var ms = new MemoryStream(data, skip, data.Length - skip - endTrim);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            deflate.CopyTo(outMs);
            var res = outMs.ToArray();
            if (res.Length > 0) return res;
        }
        catch { }

        return data;
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
}
