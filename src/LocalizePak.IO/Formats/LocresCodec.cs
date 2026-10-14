using System.Text;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Unreal Engine .locres (FTextLocalizationResource) ikili formatının gerçek yapısına uygun okuyucu/yazıcı.
/// ELocResVersion: Legacy(0), Compact(1), Optimized_CRC32(2), Optimized_CityHash64_UTF16(3).
/// Namespace/key/kaynak-metin hash değerleri okunur ve yazılırken olduğu gibi korunur;
/// yalnızca yerelleştirilmiş metinler değişir. Böylece oyun, anahtarları çeviriden sonra da bulabilir.
/// </summary>
public static class LocresCodec
{
    // FGuid(0x7574140E, 0xFC034A67, 0x9D90154A, 0x1B7F37C3) — little-endian
    private static readonly byte[] Magic =
    [
        0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC,
        0x4A, 0x15, 0x90, 0x9D, 0xC3, 0x37, 0x7F, 0x1B
    ];

    private const long MaxStringChars = 50_000_000;

    public static bool HasMagic(ReadOnlySpan<byte> data) =>
        data.Length >= 16 && data[..16].SequenceEqual(Magic);

    public static LocresFile Read(byte[] data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("Dosya çok küçük, geçerli bir .locres dosyası değil.");

        using var br = new BinaryReader(new MemoryStream(data), Encoding.UTF8, leaveOpen: false);
        var version = LocresVersion.Legacy;
        if (HasMagic(data))
        {
            br.ReadBytes(16);
            version = (LocresVersion)br.ReadByte();
            if (version > LocresVersion.V3)
                throw new InvalidDataException($"Desteklenmeyen .locres sürümü: {(byte)version}");
        }

        var file = new LocresFile { Version = version };

        string[] strings = [];
        if (version >= LocresVersion.V1)
        {
            long arrayOffset = br.ReadInt64();
            if (arrayOffset >= 0)
            {
                long resume = br.BaseStream.Position;
                br.BaseStream.Position = arrayOffset;
                int count = br.ReadInt32();
                if (count < 0 || count > 10_000_000)
                    throw new InvalidDataException($"Geçersiz yerelleştirilmiş metin sayısı: {count}");
                strings = new string[count];
                for (int i = 0; i < count; i++)
                {
                    strings[i] = ReadFString(br);
                    if (version >= LocresVersion.V2) br.ReadInt32(); // referans sayacı — yazarken yeniden hesaplanır
                }
                br.BaseStream.Position = resume;
            }
        }

        if (version >= LocresVersion.V2) br.ReadUInt32(); // toplam girdi sayısı — yazarken yeniden hesaplanır

        uint nsCount = br.ReadUInt32();
        if (nsCount > 1_000_000)
            throw new InvalidDataException($"Geçersiz namespace sayısı: {nsCount}");

        for (uint n = 0; n < nsCount; n++)
        {
            var ns = new LocresNamespace();
            if (version >= LocresVersion.V3) ns.NameHash = br.ReadUInt32();
            ns.Name = ReadFString(br);

            uint keyCount = br.ReadUInt32();
            if (keyCount > 10_000_000)
                throw new InvalidDataException($"Geçersiz anahtar sayısı: {keyCount}");

            for (uint k = 0; k < keyCount; k++)
            {
                var entry = new LocalizationEntry { NamespaceName = ns.Name };
                if (version >= LocresVersion.V3) entry.KeyHash = br.ReadUInt32();
                entry.Key = ReadFString(br);
                entry.SourceStringHash = br.ReadUInt32();

                string text;
                if (version >= LocresVersion.V1)
                {
                    int idx = br.ReadInt32();
                    text = idx >= 0 && idx < strings.Length ? strings[idx] : string.Empty;
                }
                else
                {
                    text = ReadFString(br);
                }

                entry.SourceText = text;
                entry.TranslatedText = text;
                ns.Entries.Add(entry);
            }

            file.Namespaces.Add(ns);
        }

        return file;
    }

    public static byte[] Write(LocresFile file)
    {
        var version = file.Version;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        if (version != LocresVersion.Legacy)
        {
            bw.Write(Magic);
            bw.Write((byte)version);
        }

        long arrayOffsetPos = -1;
        if (version >= LocresVersion.V1)
        {
            arrayOffsetPos = ms.Position;
            bw.Write(-1L); // yer tutucu
        }

        // Benzersiz metin tablosu (V1+)
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var table = new List<string>();
        var refCounts = new List<int>();

        int Intern(string text)
        {
            if (!index.TryGetValue(text, out int i))
            {
                i = table.Count;
                index[text] = i;
                table.Add(text);
                refCounts.Add(0);
            }
            refCounts[i]++;
            return i;
        }

        if (version >= LocresVersion.V2)
            bw.Write((uint)file.TotalEntryCount);

        bw.Write((uint)file.Namespaces.Count);
        foreach (var ns in file.Namespaces)
        {
            if (version >= LocresVersion.V3) bw.Write(ns.NameHash);
            WriteFString(bw, ns.Name);
            bw.Write((uint)ns.Entries.Count);

            foreach (var e in ns.Entries)
            {
                if (version >= LocresVersion.V3) bw.Write(e.KeyHash);
                WriteFString(bw, e.Key);
                bw.Write(e.SourceStringHash);

                var text = string.IsNullOrEmpty(e.TranslatedText) ? e.SourceText : e.TranslatedText;
                if (version >= LocresVersion.V1) bw.Write(Intern(text ?? string.Empty));
                else WriteFString(bw, text ?? string.Empty);
            }
        }

        if (version >= LocresVersion.V1)
        {
            long arrayOffset = ms.Position;
            bw.Write(table.Count);
            for (int i = 0; i < table.Count; i++)
            {
                WriteFString(bw, table[i]);
                if (version >= LocresVersion.V2) bw.Write(refCounts[i]);
            }

            bw.Flush();
            ms.Position = arrayOffsetPos;
            bw.Write(arrayOffset);
        }

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>UE FString: 0 = boş; pozitif = ANSI (Latin-1); negatif = UTF-16LE. Uzunluk sonlandırıcı null dahildir.</summary>
    public static string ReadFString(BinaryReader br)
    {
        int len = br.ReadInt32();
        if (len == 0) return string.Empty;
        if (len == int.MinValue) throw new InvalidDataException("Geçersiz FString uzunluğu.");

        if (len < 0)
        {
            long chars = -(long)len;
            if (chars > MaxStringChars) throw new InvalidDataException($"FString çok büyük: {chars}");
            var bytes = br.ReadBytes((int)chars * 2);
            if (bytes.Length != chars * 2) throw new EndOfStreamException();
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        else
        {
            if (len > MaxStringChars) throw new InvalidDataException($"FString çok büyük: {len}");
            var bytes = br.ReadBytes(len);
            if (bytes.Length != len) throw new EndOfStreamException();
            return Encoding.Latin1.GetString(bytes).TrimEnd('\0');
        }
    }

    public static void WriteFString(BinaryWriter bw, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            bw.Write(0);
            return;
        }

        bool ascii = true;
        foreach (char c in value)
        {
            if (c >= 0x80) { ascii = false; break; }
        }

        if (ascii)
        {
            bw.Write(value.Length + 1);
            bw.Write(Encoding.ASCII.GetBytes(value));
            bw.Write((byte)0);
        }
        else
        {
            bw.Write(-(value.Length + 1));
            bw.Write(Encoding.Unicode.GetBytes(value));
            bw.Write((short)0);
        }
    }
}
