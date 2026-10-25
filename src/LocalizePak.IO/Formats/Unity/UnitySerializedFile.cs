using System.Buffers.Binary;
using System.Text;

namespace LocalizePak.IO.Formats.Unity;

/// <summary>SerializedFile içindeki bir tür kaydı (sınıf kimliği + varsa TypeTree).</summary>
public sealed class UnitySerializedType
{
    public int ClassId { get; init; }
    public short ScriptTypeIndex { get; init; } = -1;
    public UnityTypeTreeNode? Tree { get; init; }

    /// <summary>[SerializeReference] tip kayıtlarında (refTypes) sınıf, ad alanı ve derleme adı.</summary>
    public string? ClassName { get; init; }
    public string? Namespace { get; init; }
    public string? AssemblyName { get; init; }
}

/// <summary>SerializedFile nesne tablosundaki bir kayıt.</summary>
public sealed class UnityObjectInfo
{
    public long PathId { get; init; }
    public long ByteStart { get; init; }
    public uint ByteSize { get; init; }
    public int ClassId { get; init; }
    public UnitySerializedType? Type { get; init; }

    /// <summary>byteStart alanının dosya içindeki konumu (yeniden yazarken yerinde güncellenir).</summary>
    internal long TableEntryPosition { get; init; }
}

/// <summary>
/// Unity SerializedFile (resources.assets, sharedassets*.assets, level*, AssetBundle içindeki CAB-* düğümleri) okuyucu.
/// Desteklenen biçim sürümleri: 14–23+ (Unity 5.x → Unity 6). Yazarken başlık + meta veri aynen korunur, yalnızca
/// nesne tablosundaki ofset/boyutlar ve dosya boyutu güncellenir; değişmeyen nesneler bayt bayt kopyalanır.
/// </summary>
public sealed class UnitySerializedFile
{
    public const int ClassIdTextAsset = 49;
    public const int ClassIdMonoBehaviour = 114;

    private readonly Stream _stream;

    public uint Version { get; }
    public bool BigEndian { get; }
    public string UnityVersion { get; } = string.Empty;
    public bool EnableTypeTree { get; }
    public long DataOffset { get; }
    public long FileSize { get; }
    public IReadOnlyList<UnitySerializedType> Types { get; }
    public IReadOnlyList<UnityObjectInfo> Objects { get; }

    /// <summary>Nesne tablosu sınıf filtresiyle okunduysa true (bu durumda <see cref="WriteTo"/> kullanılamaz).</summary>
    public bool IsFiltered { get; }

    /// <summary>[SerializeReference] ile kullanılan tipler (sürüm 20+). Okunamazsa boştur.</summary>
    public IReadOnlyList<UnitySerializedType> RefTypes { get; } = [];

    /// <summary>Başlığa bakarak akışın bir Unity SerializedFile olup olmadığını tahmin eder (konumu korur).</summary>
    public static bool LooksLikeSerializedFile(Stream s)
    {
        if (!s.CanSeek || s.Length < 48) return false;
        long pos = s.Position;
        try
        {
            s.Position = 0;
            Span<byte> h = stackalloc byte[48];
            s.ReadExactly(h);
            uint version = BinaryPrimitives.ReadUInt32BigEndian(h[8..]);
            if (version < 9 || version > 64) return false;
            if (h[16] > 1 || h[17] != 0 || h[18] != 0 || h[19] != 0) return false;

            long fileSize, dataOffset;
            if (version >= 22)
            {
                fileSize = BinaryPrimitives.ReadInt64BigEndian(h[24..]);
                dataOffset = BinaryPrimitives.ReadInt64BigEndian(h[32..]);
            }
            else
            {
                fileSize = BinaryPrimitives.ReadUInt32BigEndian(h[4..]);
                dataOffset = BinaryPrimitives.ReadUInt32BigEndian(h[12..]);
            }
            return fileSize == s.Length && dataOffset > 16 && dataOffset <= fileSize;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            s.Position = pos;
        }
    }

    /// <param name="stream">SerializedFile akışı (aranabilir).</param>
    /// <param name="keepClassIds">Verilirse nesne tablosunda yalnızca bu sınıflar tutulur (milyonlarca nesneli sahnelerde
    /// belleği korur). Bu durumda dosya yeniden yazılamaz; yazmak için filtresiz açılmalıdır.</param>
    public UnitySerializedFile(Stream stream, IReadOnlySet<int>? keepClassIds = null)
    {
        _stream = stream;
        IsFiltered = keepClassIds != null;

        // Meta veri bölümünü tek seferde belleğe al: alan alan akış okumaktan çok daha hızlı (sıkıştırılmış paketlerde özellikle)
        Span<byte> head = stackalloc byte[16];
        stream.Position = 0;
        stream.ReadExactly(head);
        long headerDataOffset = BinaryPrimitives.ReadUInt32BigEndian(head[12..]);
        if (BinaryPrimitives.ReadUInt32BigEndian(head[8..]) >= 22)
        {
            Span<byte> h22 = stackalloc byte[48];
            stream.Position = 0;
            stream.ReadExactly(h22);
            headerDataOffset = BinaryPrimitives.ReadInt64BigEndian(h22[32..]);
        }
        if (headerDataOffset <= 0 || headerDataOffset > Math.Min(stream.Length, 512L * 1024 * 1024))
            throw new InvalidDataException("SerializedFile meta veri boyutu geçersiz.");
        var metadata = new byte[headerDataOffset];
        stream.Position = 0;
        stream.ReadExactly(metadata);

        var r = new UnityBinaryReader(new MemoryStream(metadata, writable: false), bigEndian: true);

        r.ReadUInt32(); // metadataSize (eski)
        long fileSize = r.ReadUInt32();
        Version = r.ReadUInt32();
        long dataOffset = r.ReadUInt32();
        if (Version < 14)
            throw new NotSupportedException($"Çok eski Unity SerializedFile sürümü ({Version}); Unity 5 ve sonrası destekleniyor.");

        byte endianess = r.ReadByte();
        r.ReadBytes(3);
        if (Version >= 22)
        {
            r.ReadUInt32(); // metadataSize
            fileSize = r.ReadInt64();
            dataOffset = r.ReadInt64();
            r.ReadInt64(); // bilinmeyen / ayrılmış
        }
        FileSize = fileSize;
        DataOffset = dataOffset;
        if (fileSize > stream.Length || dataOffset > fileSize || dataOffset != metadata.Length)
            throw new InvalidDataException("SerializedFile başlığı dosya boyutuyla uyuşmuyor.");

        BigEndian = endianess != 0;
        r.BigEndian = BigEndian;

        UnityVersion = r.ReadCString(64);
        r.ReadInt32(); // hedef platform
        EnableTypeTree = r.ReadBoolean();

        int typeCount = r.ReadInt32();
        if (typeCount < 0 || typeCount > 100_000) throw new InvalidDataException($"Geçersiz tür sayısı: {typeCount}");
        var types = new List<UnitySerializedType>(typeCount);
        for (int i = 0; i < typeCount; i++)
            types.Add(ReadSerializedType(r, isRefType: false));
        Types = types;

        int objectCount = r.ReadInt32();
        if (objectCount < 0 || objectCount > 20_000_000) throw new InvalidDataException($"Geçersiz nesne sayısı: {objectCount}");
        var objects = new List<UnityObjectInfo>(objectCount);
        for (int i = 0; i < objectCount; i++)
        {
            r.Align(4);
            long pathId = r.ReadInt64();
            long entryPos = r.Position;
            long byteStart = Version >= 22 ? r.ReadInt64() : r.ReadUInt32();
            uint byteSize = r.ReadUInt32();
            int typeId = r.ReadInt32();
            int classId;
            UnitySerializedType? type;
            if (Version < 16)
            {
                classId = r.ReadUInt16();
                type = types.FirstOrDefault(t => t.ClassId == typeId);
            }
            else
            {
                if (typeId < 0 || typeId >= types.Count) throw new InvalidDataException($"Geçersiz tür dizini: {typeId}");
                type = types[typeId];
                classId = type.ClassId;
            }
            if (Version < 17) r.ReadInt16(); // scriptTypeIndex
            if (Version == 15 || Version == 16) r.ReadByte(); // stripped

            if (byteStart < 0 || dataOffset + byteStart + byteSize > fileSize)
                throw new InvalidDataException($"Nesne {pathId} dosya sınırları dışında.");
            if (keepClassIds != null && !keepClassIds.Contains(classId)) continue;

            objects.Add(new UnityObjectInfo
            {
                PathId = pathId, ByteStart = byteStart, ByteSize = byteSize,
                ClassId = classId, Type = type, TableEntryPosition = entryPos
            });
        }
        Objects = objects;

        // Kalan meta veri: script tipleri ve harici dosyalar atlanır (yazarken aynen kopyalanır);
        // refTypes, [SerializeReference] alanlarını çözebilmek için okunur.
        var refTypes = new List<UnitySerializedType>();
        try
        {
            int scriptCount = r.ReadInt32();
            if (scriptCount < 0 || scriptCount > 1_000_000) throw new InvalidDataException("Geçersiz script sayısı.");
            for (int i = 0; i < scriptCount; i++)
            {
                r.ReadInt32();
                r.Align(4);
                r.ReadInt64();
            }

            int externalCount = r.ReadInt32();
            if (externalCount < 0 || externalCount > 1_000_000) throw new InvalidDataException("Geçersiz harici dosya sayısı.");
            for (int i = 0; i < externalCount; i++)
            {
                r.ReadCString();
                r.ReadBytes(16);
                r.ReadInt32();
                r.ReadCString();
            }

            if (Version >= 20)
            {
                int refCount = r.ReadInt32();
                if (refCount < 0 || refCount > 100_000) throw new InvalidDataException("Geçersiz ref tip sayısı.");
                for (int i = 0; i < refCount; i++)
                    refTypes.Add(ReadSerializedType(r, isRefType: true));
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            refTypes.Clear(); // SerializeReference içeren nesneler okunamaz, diğerleri etkilenmez
        }
        RefTypes = refTypes;
    }

    /// <summary>[SerializeReference] tipini (sınıf, ad alanı, derleme) TypeTree'ye çözer.</summary>
    public UnityTypeTreeNode? ResolveRefType(string className, string ns, string assembly) =>
        (RefTypes.FirstOrDefault(t => t.ClassName == className && t.Namespace == ns && t.AssemblyName == assembly)
         ?? RefTypes.FirstOrDefault(t => t.ClassName == className && t.Namespace == ns))?.Tree;

    private UnitySerializedType ReadSerializedType(UnityBinaryReader r, bool isRefType)
    {
        int classId = r.ReadInt32();
        if (Version >= 16) r.ReadBoolean(); // isStrippedType
        short scriptTypeIndex = Version >= 17 ? r.ReadInt16() : (short)-1;
        if ((isRefType && scriptTypeIndex >= 0) || (Version < 16 && classId < 0) || (Version >= 16 && classId == ClassIdMonoBehaviour))
            r.ReadBytes(16); // scriptID
        r.ReadBytes(16); // oldTypeHash

        UnityTypeTreeNode? tree = null;
        string? className = null, ns = null, assembly = null;
        if (EnableTypeTree)
        {
            tree = ReadTypeTreeBlob(r);
            if (Version >= 21)
            {
                if (isRefType)
                {
                    className = r.ReadCString();
                    ns = r.ReadCString();
                    assembly = r.ReadCString();
                }
                else
                {
                    int depCount = r.ReadInt32();
                    if (depCount < 0 || depCount > 1_000_000) throw new InvalidDataException("Geçersiz tür bağımlılık sayısı.");
                    r.ReadBytes(depCount * 4);
                }
            }
        }

        return new UnitySerializedType
        {
            ClassId = classId, ScriptTypeIndex = scriptTypeIndex, Tree = tree,
            ClassName = className, Namespace = ns, AssemblyName = assembly
        };
    }

    private UnityTypeTreeNode ReadTypeTreeBlob(UnityBinaryReader r)
    {
        int nodeCount = r.ReadInt32();
        int stringBufferSize = r.ReadInt32();
        if (nodeCount <= 0 || nodeCount > 1_000_000 || stringBufferSize < 0 || stringBufferSize > 64 * 1024 * 1024)
            throw new InvalidDataException("Geçersiz TypeTree boyutları.");

        var raw = new (int Level, int TypeFlags, uint TypeOffset, uint NameOffset, int ByteSize, int MetaFlag)[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            r.ReadUInt16(); // sürüm
            int level = r.ReadByte();
            int typeFlags = r.ReadByte();
            uint typeOffset = r.ReadUInt32();
            uint nameOffset = r.ReadUInt32();
            int byteSize = r.ReadInt32();
            r.ReadInt32(); // index
            int metaFlag = r.ReadInt32();
            if (Version >= 19) r.ReadUInt64(); // refTypeHash
            raw[i] = (level, typeFlags, typeOffset, nameOffset, byteSize, metaFlag);
        }
        var strings = r.ReadBytes(stringBufferSize);

        var flat = new List<UnityTypeTreeNode>(nodeCount);
        foreach (var n in raw)
        {
            flat.Add(new UnityTypeTreeNode
            {
                Type = ResolveString(strings, n.TypeOffset),
                Name = ResolveString(strings, n.NameOffset),
                ByteSize = n.ByteSize,
                Level = n.Level,
                TypeFlags = n.TypeFlags,
                MetaFlag = n.MetaFlag
            });
        }
        return UnityTypeTreeNode.BuildTree(flat);
    }

    private static string ResolveString(byte[] local, uint offset)
    {
        if ((offset & 0x80000000) != 0)
            return UnityCommonStrings.TryGet(offset & 0x7FFFFFFF, out var common) ? common : $"<ortak:{offset & 0x7FFFFFFF}>";
        if (offset >= local.Length) return string.Empty;
        int end = Array.IndexOf(local, (byte)0, (int)offset);
        if (end < 0) end = local.Length;
        return Encoding.UTF8.GetString(local, (int)offset, end - (int)offset);
    }

    /// <summary>Nesnenin ham verisini okur.</summary>
    public byte[] ReadObjectData(UnityObjectInfo obj)
    {
        var data = new byte[obj.ByteSize];
        _stream.Position = DataOffset + obj.ByteStart;
        _stream.ReadExactly(data);
        return data;
    }

    /// <summary>
    /// Dosyayı yeniden yazar: <paramref name="replacements"/> (pathId → yeni veri) içindeki nesneler değişir,
    /// diğerleri aynen kopyalanır. Nesneler özgün sırasıyla ve özgün hizalamasıyla yerleştirilir.
    /// </summary>
    public void WriteTo(Stream output, IReadOnlyDictionary<long, byte[]> replacements)
    {
        if (IsFiltered)
            throw new InvalidOperationException("Filtreli açılmış SerializedFile yeniden yazılamaz; filtresiz açın.");
        var prefix = new byte[DataOffset];
        _stream.Position = 0;
        _stream.ReadExactly(prefix);

        var ordered = Objects.OrderBy(o => o.ByteStart).ToList();
        int alignment = DetectAlignment(ordered);
        long originalEnd = ordered.Count == 0 ? 0 : ordered.Max(o => o.ByteStart + o.ByteSize);
        long trailing = Math.Clamp(FileSize - DataOffset - originalEnd, 0, 4096);

        // Yeni yerleşimi hesapla ve nesne tablosunu yerinde güncelle
        var newStarts = new Dictionary<UnityObjectInfo, long>(ordered.Count);
        long cursor = 0;
        foreach (var o in ordered)
        {
            long abs = DataOffset + cursor;
            long pad = (alignment - abs % alignment) % alignment;
            cursor += pad;
            newStarts[o] = cursor;
            long size = replacements.TryGetValue(o.PathId, out var rep) ? rep.Length : o.ByteSize;
            if (size > uint.MaxValue) throw new InvalidDataException("Nesne boyutu 4 GB sınırını aşıyor.");

            WriteTableEntry(prefix, o, cursor, (uint)size);
            cursor += size;
        }
        long newFileSize = DataOffset + cursor + trailing;
        PatchHeaderFileSize(prefix, newFileSize);

        output.Write(prefix);
        cursor = 0;
        var buffer = new byte[1 << 16];
        foreach (var o in ordered)
        {
            long start = newStarts[o];
            if (start > cursor) output.Write(new byte[start - cursor]);
            if (replacements.TryGetValue(o.PathId, out var rep))
            {
                output.Write(rep);
                cursor = start + rep.Length;
            }
            else
            {
                _stream.Position = DataOffset + o.ByteStart;
                long remaining = o.ByteSize;
                while (remaining > 0)
                {
                    int n = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (n <= 0) throw new EndOfStreamException();
                    output.Write(buffer, 0, n);
                    remaining -= n;
                }
                cursor = start + o.ByteSize;
            }
        }
        if (trailing > 0) output.Write(new byte[trailing]);
    }

    private int DetectAlignment(List<UnityObjectInfo> ordered)
    {
        foreach (int a in new[] { 16, 8, 4 })
        {
            if (ordered.All(o => (DataOffset + o.ByteStart) % a == 0)) return a;
        }
        return 1;
    }

    private void WriteTableEntry(byte[] prefix, UnityObjectInfo o, long newStart, uint newSize)
    {
        int p = checked((int)o.TableEntryPosition);
        var span = prefix.AsSpan(p);
        if (Version >= 22)
        {
            if (BigEndian) BinaryPrimitives.WriteInt64BigEndian(span, newStart); else BinaryPrimitives.WriteInt64LittleEndian(span, newStart);
            span = span[8..];
        }
        else
        {
            if (newStart > uint.MaxValue) throw new InvalidDataException("SerializedFile 4 GB sınırını aşıyor.");
            if (BigEndian) BinaryPrimitives.WriteUInt32BigEndian(span, (uint)newStart); else BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)newStart);
            span = span[4..];
        }
        if (BigEndian) BinaryPrimitives.WriteUInt32BigEndian(span, newSize); else BinaryPrimitives.WriteUInt32LittleEndian(span, newSize);
    }

    private void PatchHeaderFileSize(byte[] prefix, long newFileSize)
    {
        if (Version >= 22)
        {
            BinaryPrimitives.WriteInt64BigEndian(prefix.AsSpan(24), newFileSize);
            // Eski (32-bit) alan doluysa onu da tutarlı tut
            uint legacy = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(4));
            if (legacy != 0 && newFileSize <= uint.MaxValue)
                BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(4), (uint)newFileSize);
        }
        else
        {
            if (newFileSize > uint.MaxValue) throw new InvalidDataException("SerializedFile 4 GB sınırını aşıyor.");
            BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(4), (uint)newFileSize);
        }
    }
}
