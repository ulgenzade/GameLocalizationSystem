using System.Text;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO.Helpers;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Godot Engine .pck / .pak arşiv formatı ayrıştırıcısı ve serileştiricisi (Magic: "GDPC" = 0x43504447).
/// </summary>
public static class GodotPakHandler
{
    public const uint GodotMagic = 0x43504447; // "GDPC"

    public static bool IsGodotPak(Stream stream)
    {
        if (stream.Length < 16) return false;
        long originalPos = stream.Position;
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            uint magic = br.ReadUInt32();
            return magic == GodotMagic;
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

    public static async Task<PakArchive> ReadArchiveAsync(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

        uint magic = br.ReadUInt32();
        if (magic != GodotMagic)
            throw new InvalidDataException("Geçersiz Godot PCK formatı.");

        int pckVersion = br.ReadInt32();
        int godotMajor = br.ReadInt32();
        int godotMinor = br.ReadInt32();
        int godotPatch = br.ReadInt32();

        long fileBase = 0;
        if (pckVersion >= 2)
        {
            br.ReadUInt32(); // flags
            fileBase = br.ReadInt64();
            br.ReadBytes(64); // reserved
        }
        else
        {
            br.ReadBytes(64); // reserved
        }

        int fileCount = br.ReadInt32();
        if (fileCount < 0 || fileCount > 1_000_000)
            throw new InvalidDataException($"Geçersiz Godot dosya sayısı: {fileCount}");

        var entries = new List<PakEntry>(fileCount);
        for (int i = 0; i < fileCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            int pathLen = br.ReadInt32();
            if (pathLen < 0 || pathLen > 4096) continue;

            var pathBytes = br.ReadBytes(pathLen);
            string path = Encoding.UTF8.GetString(pathBytes).TrimEnd('\0');

            long offset = br.ReadInt64() + fileBase;
            long size = br.ReadInt64();
            var md5 = br.ReadBytes(16);

            if (pckVersion >= 2)
            {
                br.ReadUInt32(); // file flags
            }

            entries.Add(new PakEntry
            {
                VirtualPath = path.Replace("res://", string.Empty).Replace('\\', '/'),
                DataOffset = offset,
                UncompressedSize = size,
                CompressedSize = size,
                Compression = CompressionMethod.None,
                Hash = md5
            });
        }

        return new PakArchive
        {
            FilePath = filePath,
            Format = PakFormat.Godot,
            Version = pckVersion,
            IsEncrypted = false,
            MountPoint = "res://",
            Entries = entries
        };
    }

    public static async Task<Stream> ExtractEntryAsync(string filePath, PakEntry entry, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        SafeIo.ValidateEntryRange(entry, fs.Length, checkUncompressedOnly: true);
        fs.Seek(entry.DataOffset, SeekOrigin.Begin);

        var data = new byte[entry.UncompressedSize];
        await fs.ReadExactlyAsync(data, ct);
        return new MemoryStream(data);
    }

    public static async Task WriteArchiveAsync(
        string outputPath,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        await using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

        // Header
        bw.Write(GodotMagic); // "GDPC"
        bw.Write(1);          // pckVersion 1
        bw.Write(4);          // major
        bw.Write(0);          // minor
        bw.Write(0);          // patch
        bw.Write(new byte[64]); // reserved
        bw.Write(files.Count);

        // Dosya verilerinin başlangıç ofseti
        // Başlık boyutu + her dosya için (4 + pathLen + 8 + 8 + 16)
        long headerSize = 4 + 4 + 4 + 4 + 4 + 64 + 4;
        foreach (var (path, _) in files)
        {
            string fullPath = path.StartsWith("res://") ? path : "res://" + path;
            var pathBytes = Encoding.UTF8.GetBytes(fullPath);
            int alignedLen = (pathBytes.Length + 3) & ~3;
            headerSize += 4 + alignedLen + 8 + 8 + 16;
        }

        // 32-byte alignment
        long dataOffset = (headerSize + 31) & ~31;

        // Dosya listesi tablosunu yaz
        long currentDataOffset = dataOffset;
        var offsets = new List<long>();
        foreach (var (path, data) in files)
        {
            string fullPath = path.StartsWith("res://") ? path : "res://" + path;
            var pathBytes = Encoding.UTF8.GetBytes(fullPath);
            int alignedLen = (pathBytes.Length + 3) & ~3;
            var alignedBytes = new byte[alignedLen];
            Array.Copy(pathBytes, alignedBytes, pathBytes.Length);

            bw.Write(alignedLen);
            bw.Write(alignedBytes);
            bw.Write(currentDataOffset);
            bw.Write((long)data.Length);
            bw.Write(new byte[16]); // dummy md5

            offsets.Add(currentDataOffset);
            currentDataOffset += (data.Length + 31) & ~31; // aligned
        }

        // Padding to dataOffset
        long padLen = dataOffset - fs.Position;
        if (padLen > 0) bw.Write(new byte[padLen]);

        // Verileri yaz
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (_, data) = files[i];
            await fs.WriteAsync(data, ct);
            long pad = ((data.Length + 31) & ~31) - data.Length;
            if (pad > 0) await fs.WriteAsync(new byte[pad], ct);
        }
    }
}
