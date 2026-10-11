using System.Text;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Classic Quake / ID Tech / Valve / GoldSrc "PACK" format işleyicisi.
/// Magic: "PACK" (0x4B434150)
/// </summary>
public sealed class ClassicPakHandler
{
    public const uint PACKMagic = 0x4B434150; // "PACK"

    public static bool IsClassicPak(Stream stream)
    {
        if (stream.Length < 12) return false;
        var pos = stream.Position;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Seek(0, SeekOrigin.Begin);
        uint magic = reader.ReadUInt32();
        stream.Seek(pos, SeekOrigin.Begin);
        return magic == PACKMagic;
    }

    public static async Task<PakArchive> ReadArchiveAsync(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != PACKMagic)
            throw new InvalidDataException("Geçerli bir klasik PACK dosyası değil.");

        uint dirOffset = reader.ReadUInt32();
        uint dirLength = reader.ReadUInt32();

        if (dirOffset < 12 || dirLength > 256 * 1024 * 1024 || (long)dirOffset + dirLength > fs.Length)
            throw new InvalidDataException("Geçersiz klasik PACK dizin ofseti veya boyutu.");

        int entryCount = (int)(dirLength / 64);
        if (entryCount < 0 || entryCount > 500_000)
            throw new InvalidDataException($"Geçersiz klasik PACK girdi sayısı: {entryCount}");

        fs.Seek(dirOffset, SeekOrigin.Begin);

        var entries = new List<PakEntry>(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            byte[] nameBytes = reader.ReadBytes(56);
            int nullIdx = Array.IndexOf<byte>(nameBytes, 0);
            int nameLen = nullIdx >= 0 ? nullIdx : nameBytes.Length;
            string name = nameLen > 0 ? Encoding.ASCII.GetString(nameBytes, 0, nameLen) : $"file_{i}";

            uint offset = reader.ReadUInt32();
            uint size = reader.ReadUInt32();

            if (offset > fs.Length || (long)offset + size > fs.Length)
                continue; // Bozuk girdiyi atla

            entries.Add(new PakEntry
            {
                VirtualPath = name.Replace('\\', '/'),
                DataOffset = offset,
                CompressedSize = size,
                UncompressedSize = size,
                Compression = CompressionMethod.None
            });
        }

        return new PakArchive
        {
            FilePath = filePath,
            Version = 1,
            MountPoint = "Game",
            IsEncrypted = false,
            Entries = entries
        };
    }

    public static async Task<Stream> ExtractEntryAsync(string pakFilePath, PakEntry entry, CancellationToken ct = default)
    {
        await using var fs = new FileStream(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (entry.DataOffset < 0 || entry.UncompressedSize < 0 || entry.UncompressedSize > 1024L * 1024 * 1024 ||
            entry.DataOffset + entry.UncompressedSize > fs.Length)
        {
            throw new InvalidDataException($"Geçersiz girdi ofseti veya boyutu: {entry.VirtualPath}");
        }

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
        using var writer = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);

        // Header: 12 bytes: Magic (4) + DirOffset (4) + DirLength (4)
        writer.Write(PACKMagic);
        writer.Write((uint)0); // DirOffset placeholder
        writer.Write((uint)0); // DirLength placeholder

        var entries = new List<(string Name, uint Offset, uint Size)>();

        foreach (var (path, data) in files)
        {
            ct.ThrowIfCancellationRequested();
            uint offset = (uint)fs.Position;
            await fs.WriteAsync(data, ct);
            entries.Add((path, offset, (uint)data.Length));
        }

        uint dirOffset = (uint)fs.Position;
        foreach (var (name, offset, size) in entries)
        {
            byte[] nameBytes = new byte[56];
            byte[] rawName = Encoding.ASCII.GetBytes(name);
            Array.Copy(rawName, nameBytes, Math.Min(rawName.Length, 55));
            writer.Write(nameBytes);
            writer.Write(offset);
            writer.Write(size);
        }
        uint dirLength = (uint)(entries.Count * 64);

        // Header güncelle
        fs.Seek(4, SeekOrigin.Begin);
        writer.Write(dirOffset);
        writer.Write(dirLength);
    }
}
