using System.Text;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Unity Engine AssetBundle ve UnityFS tabanlı .pak arşiv formatı ayrıştırıcısı (Magic: "UnityFS").
/// </summary>
public static class UnityPakHandler
{
    private static readonly byte[] UnityFsMagic = "UnityFS"u8.ToArray();
    private static readonly byte[] UnityWebMagic = "UnityWeb"u8.ToArray();

    public static bool IsUnityPak(Stream stream)
    {
        if (stream.Length < 16) return false;
        long originalPos = stream.Position;
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            var buffer = new byte[8];
            int read = stream.Read(buffer, 0, 8);
            if (read < 7) return false;

            return buffer.Take(7).SequenceEqual(UnityFsMagic) ||
                   buffer.Take(8).SequenceEqual(UnityWebMagic);
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

        // Read signature
        var sigBytes = new List<byte>();
        while (sigBytes.Count < 32)
        {
            byte b = br.ReadByte();
            if (b == 0) break;
            sigBytes.Add(b);
        }
        string signature = Encoding.ASCII.GetString(sigBytes.ToArray());

        int formatVersion = br.ReadInt32();
        string unityVersion = ReadNullTerminatedString(br);
        string generatorVersion = ReadNullTerminatedString(br);
        long fileSize = br.ReadInt64();
        int compressedBlocksInfoSize = br.ReadInt32();
        int uncompressedBlocksInfoSize = br.ReadInt32();
        int flags = br.ReadInt32();

        long remainingSize = Math.Max(0, (fileSize > 0 ? fileSize : fs.Length) - fs.Position);
        var entries = new List<PakEntry>
        {
            new PakEntry
            {
                VirtualPath = "Assets/Data.unity3d",
                UncompressedSize = remainingSize,
                CompressedSize = remainingSize,
                DataOffset = fs.Position,
                Compression = CompressionMethod.None
            }
        };

        return new PakArchive
        {
            FilePath = filePath,
            Format = PakFormat.Unity,
            Version = formatVersion,
            IsEncrypted = false,
            MountPoint = "Assets/",
            Entries = entries
        };
    }

    public static async Task<Stream> ExtractEntryAsync(string filePath, PakEntry entry, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        fs.Seek(entry.DataOffset, SeekOrigin.Begin);
        var data = new byte[entry.UncompressedSize];
        await fs.ReadExactlyAsync(data, ct);
        return new MemoryStream(data);
    }

    private static string ReadNullTerminatedString(BinaryReader br)
    {
        var sb = new StringBuilder();
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            byte b = br.ReadByte();
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    public static async Task WriteArchiveAsync(
        string outputPath,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

        // UnityFS signature
        writer.Write("UnityFS\0"u8.ToArray());
        writer.Write((int)6); // FormatVersion
        writer.Write("5.x.x\0"u8.ToArray());
        writer.Write("2021.3.0f1\0"u8.ToArray());

        long sizePos = fs.Position;
        writer.Write((long)0); // FileSize placeholder
        writer.Write((int)0);  // CompressedBlocksInfoSize
        writer.Write((int)0);  // UncompressedBlocksInfoSize
        writer.Write((int)0x40); // Flags

        foreach (var (_, data) in files)
        {
            ct.ThrowIfCancellationRequested();
            await fs.WriteAsync(data, ct);
        }

        long totalSize = fs.Position;
        fs.Seek(sizePos, SeekOrigin.Begin);
        writer.Write(totalSize);
    }
}
