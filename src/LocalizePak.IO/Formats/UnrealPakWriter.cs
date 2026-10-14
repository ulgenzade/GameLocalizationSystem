using System.Security.Cryptography;
using System.Text;
using LocalizePak.IO.Helpers;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Unreal Engine 4.22+ tarafından yüklenebilen, sıkıştırmasız ve (isteğe bağlı) indeksi şifreli .pak (sürüm 8) yazıcısı.
/// Yama paketleri (<c>*_P.pak</c>) için tasarlanmıştır. Dosya yerleşimi:
///   [FPakEntry başlığı + veri] x N, indeks (mount point, girdiler), footer (221 bayt).
/// </summary>
public static class UnrealPakWriter
{
    public const uint Magic = 0x5A6F12E1;
    public const int Version = 8;
    private const int CompressionNameLen = 32;
    private const int MaxCompressionMethods = 5;

    public static void Write(
        Stream output,
        string mountPoint,
        IReadOnlyList<(string VirtualPath, byte[] Data)> files,
        byte[]? aesKey = null)
    {
        if (string.IsNullOrWhiteSpace(mountPoint)) mountPoint = "../../../";
        if (!mountPoint.EndsWith('/')) mountPoint += "/";

        var index = new List<(string Path, long Offset, long Size, byte[] Hash)>();
        using var bw = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        long start = output.Position;

        foreach (var (path, data) in files)
        {
            long entryOffset = output.Position - start;
            var hash = SHA1.HashData(data);
            WriteEntry(bw, offset: 0, data.Length, hash);
            bw.Write(data);
            index.Add((path.Replace('\\', '/').TrimStart('/'), entryOffset, data.Length, hash));
        }

        // İndeks
        using var ims = new MemoryStream();
        using (var iw = new BinaryWriter(ims, Encoding.UTF8, leaveOpen: true))
        {
            WriteFString(iw, mountPoint);
            iw.Write(index.Count);
            foreach (var e in index)
            {
                WriteFString(iw, e.Path);
                WriteEntry(iw, e.Offset, e.Size, e.Hash);
            }
        }

        var plainIndex = ims.ToArray();
        var indexHash = SHA1.HashData(plainIndex);
        byte[] indexBytes = plainIndex;
        bool encrypted = aesKey is { Length: 32 };
        if (encrypted) indexBytes = AesDecryptionHelper.EncryptEcb(plainIndex, aesKey!);

        long indexOffset = output.Position - start;
        bw.Write(indexBytes);

        // Footer (221 bayt): guid, şifreli bayrağı, magic, sürüm, indeks ofseti/boyutu/hash'i, sıkıştırma adları
        bw.Write(new byte[16]);
        bw.Write((byte)(encrypted ? 1 : 0));
        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(indexOffset);
        bw.Write((long)indexBytes.Length);
        bw.Write(indexHash);
        bw.Write(new byte[CompressionNameLen * MaxCompressionMethods]);
        bw.Flush();
    }

    private static void WriteEntry(BinaryWriter w, long offset, long size, byte[] hash)
    {
        w.Write(offset);
        w.Write(size);          // sıkıştırılmış boyut
        w.Write(size);          // açılmış boyut
        w.Write(0);             // sıkıştırma yöntemi (0 = yok)
        w.Write(hash);          // SHA1 (20 bayt)
        w.Write((byte)0);       // bayraklar (şifreli/silinmiş değil)
        w.Write(0u);            // sıkıştırma blok boyutu
    }

    private static void WriteFString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write(bytes.Length + 1);
        w.Write(bytes);
        w.Write((byte)0);
    }
}
