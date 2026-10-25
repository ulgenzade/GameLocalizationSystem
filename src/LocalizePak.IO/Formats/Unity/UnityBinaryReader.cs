using System.Buffers.Binary;
using System.Text;

namespace LocalizePak.IO.Formats.Unity;

/// <summary>
/// Unity dosyaları için endian ayarlı ikili okuyucu. UnityFS başlığı ve SerializedFile başlığı big-endian,
/// SerializedFile meta verisi ve nesne verisi genellikle little-endian'dır.
/// </summary>
internal sealed class UnityBinaryReader
{
    private readonly Stream _stream;
    private readonly byte[] _buf = new byte[8];

    public bool BigEndian { get; set; }

    public UnityBinaryReader(Stream stream, bool bigEndian)
    {
        _stream = stream;
        BigEndian = bigEndian;
    }

    public Stream BaseStream => _stream;

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    private ReadOnlySpan<byte> Fill(int count)
    {
        _stream.ReadExactly(_buf, 0, count);
        return _buf.AsSpan(0, count);
    }

    public byte ReadByte() => Fill(1)[0];
    public bool ReadBoolean() => ReadByte() != 0;
    public short ReadInt16() => BigEndian ? BinaryPrimitives.ReadInt16BigEndian(Fill(2)) : BinaryPrimitives.ReadInt16LittleEndian(Fill(2));
    public ushort ReadUInt16() => BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(Fill(2)) : BinaryPrimitives.ReadUInt16LittleEndian(Fill(2));
    public int ReadInt32() => BigEndian ? BinaryPrimitives.ReadInt32BigEndian(Fill(4)) : BinaryPrimitives.ReadInt32LittleEndian(Fill(4));
    public uint ReadUInt32() => BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(Fill(4)) : BinaryPrimitives.ReadUInt32LittleEndian(Fill(4));
    public long ReadInt64() => BigEndian ? BinaryPrimitives.ReadInt64BigEndian(Fill(8)) : BinaryPrimitives.ReadInt64LittleEndian(Fill(8));
    public ulong ReadUInt64() => BigEndian ? BinaryPrimitives.ReadUInt64BigEndian(Fill(8)) : BinaryPrimitives.ReadUInt64LittleEndian(Fill(8));

    public byte[] ReadBytes(int count)
    {
        if (count < 0) throw new InvalidDataException($"Geçersiz bayt sayısı: {count}");
        var data = new byte[count];
        _stream.ReadExactly(data);
        return data;
    }

    /// <summary>Null ile sonlanan UTF-8 metin (en fazla <paramref name="maxLength"/> bayt).</summary>
    public string ReadCString(int maxLength = 32768)
    {
        var bytes = new List<byte>();
        while (bytes.Count < maxLength)
        {
            int b = _stream.ReadByte();
            if (b <= 0) break;
            bytes.Add((byte)b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>Konumu verilen hizaya (4, 8, 16) yuvarlar.</summary>
    public void Align(int alignment)
    {
        long mod = _stream.Position % alignment;
        if (mod != 0) _stream.Position += alignment - mod;
    }
}
