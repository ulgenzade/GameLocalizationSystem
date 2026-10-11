using System.IO.Compression;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// CryEngine, Lumberyard, Far Cry, Godot ve standart ZIP tabanlı .pak arşivlerini açan servis.
/// Magic: "PK\x03\x04" (0x04034B50) veya PK Central Directory
/// </summary>
public sealed class ZipPakHandler
{
    public const uint PKZipMagic = 0x04034B50;

    public static bool IsZipPak(Stream stream)
    {
        if (stream.Length < 4) return false;
        var pos = stream.Position;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        stream.Seek(0, SeekOrigin.Begin);
        uint magic = reader.ReadUInt32();
        stream.Seek(pos, SeekOrigin.Begin);
        return magic == PKZipMagic;
    }

    public static Task<PakArchive> ReadArchiveAsync(string filePath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(filePath);
        var entries = new List<PakEntry>();

        long offsetCounter = 0;
        foreach (var zEntry in zip.Entries)
        {
            if (string.IsNullOrEmpty(zEntry.Name)) continue; // klasörleri atla

            entries.Add(new PakEntry
            {
                // Not: VirtualPath yalnızca arşiv içi sanal yol olarak kullanılır, diske yazılmaz.
                // Orijinal adı birebir korumak, ExtractEntryAsync içindeki GetEntry eşleşmesi için zorunludur.
                VirtualPath = zEntry.FullName.Replace('\\', '/'),
                DataOffset = offsetCounter++,
                CompressedSize = zEntry.CompressedLength,
                UncompressedSize = zEntry.Length,
                Compression = zEntry.CompressedLength == zEntry.Length ? CompressionMethod.None : CompressionMethod.Zlib
            });
        }

        var archive = new PakArchive
        {
            FilePath = filePath,
            Version = 1,
            MountPoint = "GameData",
            IsEncrypted = false,
            Entries = entries
        };

        return Task.FromResult(archive);
    }

    public static async Task<Stream> ExtractEntryAsync(string pakFilePath, PakEntry entry, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(pakFilePath);
        var zEntry = zip.GetEntry(entry.VirtualPath)
                     ?? zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(entry.VirtualPath, StringComparison.OrdinalIgnoreCase));

        if (zEntry is null)
            throw new FileNotFoundException($"ZIP arşivi içinde dosya bulunamadı: {entry.VirtualPath}");

        var ms = new MemoryStream();
        await using (var entryStream = zEntry.Open())
        {
            await entryStream.CopyToAsync(ms, ct);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Verilen dosyaları ZIP formatında yeni bir .pak arşivine yazar.
    /// </summary>
    public static async Task WriteArchiveAsync(
        string outputPath,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        CancellationToken ct = default)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var (virtualPath, data) in files)
        {
            ct.ThrowIfCancellationRequested();
            var entry = zip.CreateEntry(virtualPath, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(data, ct);
        }
    }
}
