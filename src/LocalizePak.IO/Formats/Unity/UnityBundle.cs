using System.Buffers.Binary;
using System.Text;
using K4os.Compression.LZ4;

namespace LocalizePak.IO.Formats.Unity;

/// <summary>UnityFS paketindeki bir sanal dosya (genellikle "CAB-..." SerializedFile veya .resS).</summary>
public sealed record UnityBundleNode(long Offset, long Size, uint Flags, string Path);

/// <summary>Sıkıştırılmış veri bloğu (blocksInfo kaydı).</summary>
internal sealed record UnityStorageBlock(uint UncompressedSize, uint CompressedSize, ushort Flags)
{
    public int Compression => Flags & 0x3F;
}

/// <summary>
/// UnityFS AssetBundle okuyucu / yazıcı (format sürümü 6–8; Unity 5.3 → Unity 6).
/// Veri blokları istendikçe açılır; böylece GB boyutundaki data.unity3d dosyaları belleğe tamamen alınmaz.
/// Yazarken yalnızca değişen düğümler değişir; tüm veri 128 KB'lık LZ4 blokları halinde yeniden sıkıştırılır.
/// </summary>
public sealed class UnityBundle : IDisposable
{
    private const uint FlagBlocksInfoAtEnd = 0x80;
    private const uint FlagBlockInfoNeedPaddingAtStart = 0x200;
    private const int OutputBlockSize = 0x20000; // Unity'nin LZ4 parça boyutu (128 KB)

    private readonly Stream _source;
    private readonly bool _ownsSource;
    private readonly List<UnityStorageBlock> _blocks = [];
    private readonly long _dataStart;

    public uint FormatVersion { get; }
    public string UnityVersion { get; }
    public string UnityRevision { get; }
    public uint Flags { get; }
    public byte[] UncompressedDataHash { get; } = new byte[16];
    public IReadOnlyList<UnityBundleNode> Nodes { get; }

    /// <summary>Tüm düğümleri içeren, istendikçe açılan sanal veri akışı.</summary>
    internal BundleDataStream Data { get; }

    public static bool HasUnityFsSignature(ReadOnlySpan<byte> head) =>
        head.Length >= 8 && head[..8].SequenceEqual("UnityFS\0"u8);

    public static UnityBundle Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16), ownsSource: true);

    public UnityBundle(Stream source, bool ownsSource = false)
    {
        _source = source;
        _ownsSource = ownsSource;
        var r = new UnityBinaryReader(source, bigEndian: true) { Position = 0 };

        var signature = r.ReadCString(16);
        if (signature != "UnityFS")
            throw new NotSupportedException($"Desteklenmeyen Unity paket imzası: '{signature}' (yalnızca UnityFS destekleniyor).");

        FormatVersion = r.ReadUInt32();
        if (FormatVersion < 6 || FormatVersion > 8)
            throw new NotSupportedException($"Desteklenmeyen UnityFS sürümü: {FormatVersion}");

        UnityVersion = r.ReadCString(64);
        UnityRevision = r.ReadCString(64);
        long declaredSize = r.ReadInt64();
        uint compressedInfoSize = r.ReadUInt32();
        uint uncompressedInfoSize = r.ReadUInt32();
        Flags = r.ReadUInt32();
        if (FormatVersion >= 7) r.Align(16);

        if (compressedInfoSize > 64 * 1024 * 1024 || uncompressedInfoSize > 256 * 1024 * 1024)
            throw new InvalidDataException("UnityFS blok tablosu boyutu geçersiz.");

        byte[] compressedInfo;
        if ((Flags & FlagBlocksInfoAtEnd) != 0)
        {
            long resume = r.Position;
            r.Position = source.Length - compressedInfoSize;
            compressedInfo = r.ReadBytes((int)compressedInfoSize);
            r.Position = resume;
        }
        else
        {
            compressedInfo = r.ReadBytes((int)compressedInfoSize);
        }

        if ((Flags & FlagBlockInfoNeedPaddingAtStart) != 0) r.Align(16);
        _dataStart = r.Position;

        var info = Decompress(compressedInfo, (int)uncompressedInfoSize, (int)(Flags & 0x3F));
        var ir = new UnityBinaryReader(new MemoryStream(info), bigEndian: true);
        ir.ReadBytes(16).CopyTo(UncompressedDataHash, 0);

        int blockCount = ir.ReadInt32();
        if (blockCount < 0 || blockCount > 10_000_000) throw new InvalidDataException("Geçersiz UnityFS blok sayısı.");
        for (int i = 0; i < blockCount; i++)
            _blocks.Add(new UnityStorageBlock(ir.ReadUInt32(), ir.ReadUInt32(), ir.ReadUInt16()));

        int nodeCount = ir.ReadInt32();
        if (nodeCount < 0 || nodeCount > 1_000_000) throw new InvalidDataException("Geçersiz UnityFS düğüm sayısı.");
        var nodes = new List<UnityBundleNode>(nodeCount);
        for (int i = 0; i < nodeCount; i++)
            nodes.Add(new UnityBundleNode(ir.ReadInt64(), ir.ReadInt64(), ir.ReadUInt32(), ir.ReadCString(4096)));
        Nodes = nodes;

        Data = new BundleDataStream(source, _dataStart, _blocks);
        foreach (var n in nodes)
        {
            if (n.Offset < 0 || n.Size < 0 || n.Offset + n.Size > Data.Length)
                throw new InvalidDataException($"UnityFS düğümü veri sınırları dışında: {n.Path}");
        }
    }

    /// <summary>Bir düğümün içeriğini okumak için salt-okunur, aranabilir akış.</summary>
    public Stream OpenNode(UnityBundleNode node) => new SubStream(Data, node.Offset, node.Size);

    /// <summary>
    /// Paketi yeniden yazar. <paramref name="replacements"/> içinde olan düğümler verilen yazıcıyla üretilir,
    /// diğerleri kaynak paketten aynen kopyalanır.
    /// </summary>
    public void Write(Stream output, IReadOnlyDictionary<string, Action<Stream>> replacements)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"gls-unityfs-{Guid.NewGuid():N}.tmp");
        try
        {
            var newNodes = new List<UnityBundleNode>(Nodes.Count);
            var newBlocks = new List<UnityStorageBlock>();
            using (var temp = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose))
            {
                using (var writer = new BlockWriter(temp, newBlocks))
                {
                    foreach (var node in Nodes)
                    {
                        long start = writer.Position;
                        if (replacements.TryGetValue(node.Path, out var produce))
                        {
                            produce(writer);
                        }
                        else
                        {
                            using var src = OpenNode(node);
                            src.CopyTo(writer);
                        }
                        newNodes.Add(node with { Offset = start, Size = writer.Position - start });
                    }
                }

                // blocksInfo (big-endian)
                using var infoMs = new MemoryStream();
                var tmp = new byte[8];
                infoMs.Write(UncompressedDataHash);
                WriteBE32(infoMs, newBlocks.Count, tmp);
                foreach (var b in newBlocks)
                {
                    WriteBE32(infoMs, (int)b.UncompressedSize, tmp);
                    WriteBE32(infoMs, (int)b.CompressedSize, tmp);
                    BinaryPrimitives.WriteUInt16BigEndian(tmp, b.Flags);
                    infoMs.Write(tmp, 0, 2);
                }
                WriteBE32(infoMs, newNodes.Count, tmp);
                foreach (var n in newNodes)
                {
                    WriteBE64(infoMs, n.Offset, tmp);
                    WriteBE64(infoMs, n.Size, tmp);
                    WriteBE32(infoMs, (int)n.Flags, tmp);
                    infoMs.Write(Encoding.UTF8.GetBytes(n.Path));
                    infoMs.WriteByte(0);
                }

                var infoRaw = infoMs.ToArray();
                var infoCompressed = TryCompressLz4(infoRaw, out var infoComp) ? infoComp : infoRaw;
                int infoCompression = ReferenceEquals(infoCompressed, infoRaw) ? 0 : 2;
                uint flags = (Flags & ~0x3Fu & ~FlagBlocksInfoAtEnd) | (uint)infoCompression;

                // Başlık
                using var header = new MemoryStream();
                header.Write("UnityFS\0"u8);
                WriteBE32(header, (int)FormatVersion, tmp);
                header.Write(Encoding.UTF8.GetBytes(UnityVersion)); header.WriteByte(0);
                header.Write(Encoding.UTF8.GetBytes(UnityRevision)); header.WriteByte(0);
                long sizePos = header.Position;
                WriteBE64(header, 0, tmp); // toplam boyut, aşağıda düzeltilir
                WriteBE32(header, infoCompressed.Length, tmp);
                WriteBE32(header, infoRaw.Length, tmp);
                WriteBE32(header, (int)flags, tmp);
                if (FormatVersion >= 7) PadTo(header, 16);
                header.Write(infoCompressed);
                if ((flags & FlagBlockInfoNeedPaddingAtStart) != 0) PadTo(header, 16);

                long total = header.Length + temp.Length;
                header.Position = sizePos;
                WriteBE64(header, total, tmp);

                header.Position = 0;
                header.CopyTo(output);
                temp.Position = 0;
                temp.CopyTo(output);
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException) { }
        }
    }

    internal static byte[] Decompress(byte[] data, int uncompressedSize, int compression)
    {
        switch (compression)
        {
            case 0:
                return data;
            case 2:
            case 3:
                var output = new byte[uncompressedSize];
                int decoded = LZ4Codec.Decode(data, 0, data.Length, output, 0, output.Length);
                if (decoded != uncompressedSize)
                    throw new InvalidDataException($"LZ4 bloğu açılamadı ({decoded}/{uncompressedSize} bayt).");
                return output;
            case 1:
                throw new NotSupportedException("LZMA sıkıştırmalı Unity paketleri henüz desteklenmiyor (LZ4/LZ4HC destekleniyor).");
            default:
                throw new NotSupportedException($"Desteklenmeyen Unity sıkıştırma türü: {compression}");
        }
    }

    private static bool TryCompressLz4(byte[] raw, out byte[] compressed)
    {
        var target = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
        int len = LZ4Codec.Encode(raw, 0, raw.Length, target, 0, target.Length, LZ4Level.L09_HC);
        if (len <= 0 || len >= raw.Length)
        {
            compressed = raw;
            return false;
        }
        compressed = target.AsSpan(0, len).ToArray();
        return true;
    }

    private static void WriteBE32(Stream s, int v, byte[] tmp) { BinaryPrimitives.WriteInt32BigEndian(tmp, v); s.Write(tmp, 0, 4); }
    private static void WriteBE64(Stream s, long v, byte[] tmp) { BinaryPrimitives.WriteInt64BigEndian(tmp, v); s.Write(tmp, 0, 8); }

    private static void PadTo(Stream s, int alignment)
    {
        long mod = s.Position % alignment;
        if (mod != 0) s.Write(new byte[alignment - mod]);
    }

    public void Dispose()
    {
        if (_ownsSource) _source.Dispose();
    }

    /// <summary>Yazılan veriyi 128 KB'lık bloklar halinde LZ4 ile sıkıştırıp hedefe yazar.</summary>
    private sealed class BlockWriter(Stream target, List<UnityStorageBlock> blocks) : Stream
    {
        private readonly byte[] _buffer = new byte[OutputBlockSize];
        private int _filled;
        private long _position;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                int n = Math.Min(data.Length, _buffer.Length - _filled);
                data[..n].CopyTo(_buffer.AsSpan(_filled));
                _filled += n;
                _position += n;
                data = data[n..];
                if (_filled == _buffer.Length) FlushBlock();
            }
        }

        private void FlushBlock()
        {
            if (_filled == 0) return;
            var raw = _buffer.AsSpan(0, _filled).ToArray();
            if (TryCompressLz4(raw, out var compressed))
            {
                target.Write(compressed);
                blocks.Add(new UnityStorageBlock((uint)raw.Length, (uint)compressed.Length, 3)); // LZ4HC
            }
            else
            {
                target.Write(raw);
                blocks.Add(new UnityStorageBlock((uint)raw.Length, (uint)raw.Length, 0));
            }
            _filled = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) FlushBlock();
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _position;
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>UnityFS veri bloklarını istendikçe açan, aranabilir salt-okunur akış (son açılan blok önbellekte tutulur).</summary>
internal sealed class BundleDataStream : Stream
{
    private readonly Stream _source;
    private readonly List<UnityStorageBlock> _blocks;
    private readonly long[] _uncompressedStarts;
    private readonly long[] _compressedStarts;
    private int _cachedIndex = -1;
    private byte[] _cached = [];
    private long _position;

    public BundleDataStream(Stream source, long dataStart, List<UnityStorageBlock> blocks)
    {
        _source = source;
        _blocks = blocks;
        _uncompressedStarts = new long[blocks.Count];
        _compressedStarts = new long[blocks.Count];
        long u = 0, c = dataStart;
        for (int i = 0; i < blocks.Count; i++)
        {
            _uncompressedStarts[i] = u;
            _compressedStarts[i] = c;
            u += blocks[i].UncompressedSize;
            c += blocks[i].CompressedSize;
        }
        Length = u;
        if (c > source.Length)
            throw new InvalidDataException("UnityFS veri blokları dosya boyutunu aşıyor (eksik veya bozuk paket).");
    }

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => _position = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        int total = 0;
        while (!destination.IsEmpty && _position < Length)
        {
            int idx = FindBlock(_position);
            long within = _position - _uncompressedStarts[idx];

            if (_blocks[idx].Compression == 0)
            {
                // Sıkıştırılmamış blok (çoğu zaman yüzlerce MB'lık .resS/.resource): belleğe almadan doğrudan oku
                int direct = (int)Math.Min(destination.Length, _blocks[idx].UncompressedSize - within);
                _source.Position = _compressedStarts[idx] + within;
                _source.ReadExactly(destination[..direct]);
                destination = destination[direct..];
                _position += direct;
                total += direct;
                continue;
            }

            var block = LoadBlock(idx);
            int n = Math.Min(destination.Length, block.Length - (int)within);
            block.AsSpan((int)within, n).CopyTo(destination);
            destination = destination[n..];
            _position += n;
            total += n;
        }
        return total;
    }

    private int FindBlock(long position)
    {
        int idx = Array.BinarySearch(_uncompressedStarts, position);
        return idx >= 0 ? idx : ~idx - 1;
    }

    private byte[] LoadBlock(int index)
    {
        if (index == _cachedIndex) return _cached;
        var b = _blocks[index];
        var compressed = new byte[b.CompressedSize];
        _source.Position = _compressedStarts[index];
        _source.ReadExactly(compressed);
        _cached = UnityBundle.Decompress(compressed, (int)b.UncompressedSize, b.Compression);
        _cachedIndex = index;
        return _cached;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset
    };

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Başka bir akışın [başlangıç, başlangıç+uzunluk) aralığını gösteren salt-okunur pencere.</summary>
internal sealed class SubStream(Stream inner, long start, long length) : Stream
{
    private long _position;

    public override long Length => length;
    public override long Position
    {
        get => _position;
        set => _position = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        long remaining = length - _position;
        if (remaining <= 0) return 0;
        if (destination.Length > remaining) destination = destination[..(int)remaining];
        inner.Position = start + _position;
        int n = inner.Read(destination);
        _position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => length + offset
    };

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
