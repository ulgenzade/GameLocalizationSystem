using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.Utils;

namespace LocalizePak.IO.Formats.IoStore;

/// <summary>Konteynere yazılacak paket (zen) için paket deposu bilgisi.</summary>
public sealed class IoStorePackageInfo
{
    public required ulong PackageId { get; init; }
    public IReadOnlyList<ulong> ImportedPackages { get; init; } = [];

    /// <summary>Yalnızca eski konteyner başlıkları (NoExportInfo öncesi, UE 5.0–5.2) için.</summary>
    public int ExportCount { get; init; }
    public int ExportBundleCount { get; init; }
}

/// <summary>Konteynere yazılacak tek bir parça (chunk).</summary>
public sealed class IoStoreChunk
{
    /// <summary>12 baytlık FIoChunkId: 8 bayt kimlik, 2 bayt dizin (ağ bayt sırası), 1 bayt dolgu, 1 bayt tür.</summary>
    public required ulong Id { get; init; }
    public ushort Index { get; init; }
    public required EIoChunkType5 Type { get; init; }
    public required byte[] Data { get; init; }

    /// <summary>Dizin indeksindeki yolu (ör. "Game/Content/UI/WBP_Menu.uasset"); null ise dizine eklenmez.</summary>
    public string? Path { get; init; }

    /// <summary>Paket verisi (ExportBundleData) ise paket deposu girdisi.</summary>
    public IoStorePackageInfo? Package { get; init; }
}

/// <summary>
/// UE5 IoStore yama konteyneri (<c>*_P.utoc</c> + <c>*_P.ucas</c>) yazıcısı. Sıkıştırmasız, şifresiz ve imzasız
/// konteyner üretir; parçalar blok boyutuna hizalanır, dizin indeksi ve konteyner başlığı (paket deposu) yazılır.
/// <para>
/// IoStore kullanan oyunlarda motor bir <c>.pak</c> bağlarken aynı adlı <c>.utoc</c>'u da arar; yalnızca .locres içeren
/// bir yama bile bu eşle (boş konteyner) birlikte dağıtılır. Paket parçaları (zen .uasset) eklenirse kaynak konteynerdeki
/// paket kimliği ve içe aktarma listesiyle paket deposuna kaydedilir; yüksek öncelikli (_P) konteyner aynı parçaları ezer.
/// </para>
/// </summary>
public static class IoStoreContainerWriter
{
    public const uint DefaultCompressionBlockSize = 64 * 1024;
    private const int TocHeaderSize = 144;
    private const int CompressionMethodNameLength = 32;
    private const uint ContainerHeaderSignature = 0x496f436e;
    private const uint InvalidHandle = uint.MaxValue;
    private static readonly byte[] TocMagic = "-==--==--==--==-"u8.ToArray();

    /// <summary>
    /// <paramref name="basePath"/>.utoc ve .ucas dosyalarını yazar.
    /// </summary>
    /// <param name="basePath">Uzantısız yol (ör. <c>…/pakchunk0-Windows_P</c>); konteyner kimliği dosya adından türetilir.</param>
    /// <param name="tocVersion">Kaynak oyunun .utoc sürümü (motor kendi sürümünden yenisini reddeder).</param>
    /// <param name="containerHeaderVersion">Kaynak oyunun konteyner başlığı sürümü (EIoContainerHeaderVersion).</param>
    public static void Write(
        string basePath,
        IReadOnlyList<IoStoreChunk> chunks,
        EIoStoreTocVersion tocVersion,
        EIoContainerHeaderVersion containerHeaderVersion,
        string mountPoint = "../../../",
        uint compressionBlockSize = DefaultCompressionBlockSize)
    {
        if (tocVersion < EIoStoreTocVersion.DirectoryIndex)
            throw new NotSupportedException($"IoStore TOC sürümü {tocVersion} yazılamıyor (UE 4.26+ gerekir).");
        if (compressionBlockSize == 0 || compressionBlockSize > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(compressionBlockSize));

        var containerName = Path.GetFileName(basePath);
        ulong containerId = ContainerIdFromName(containerName);

        var all = new List<IoStoreChunk>(chunks.Count + 1);
        all.AddRange(chunks);
        all.Add(new IoStoreChunk
        {
            Id = containerId,
            Type = EIoChunkType5.ContainerHeader,
            Data = BuildContainerHeader(containerId, chunks.Where(c => c.Package != null).ToList(), containerHeaderVersion)
        });

        var duplicate = all.GroupBy(c => (c.Id, c.Index, c.Type)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"Aynı parça kimliği iki kez eklendi: {duplicate.Key}");

        // .ucas: her parça blok sınırından başlar, bloklar sıkıştırmasız ardışık yazılır
        var offsetLengths = new List<(ulong Offset, ulong Length)>(all.Count);
        var blocks = new List<(long CasOffset, uint Size)>();
        using (var cas = new FileStream(basePath + ".ucas", FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            foreach (var chunk in all)
            {
                ulong uncompressedOffset = (ulong)blocks.Count * compressionBlockSize;
                offsetLengths.Add((uncompressedOffset, (ulong)chunk.Data.Length));
                for (int pos = 0; pos < chunk.Data.Length; pos += (int)compressionBlockSize)
                {
                    int size = (int)Math.Min(compressionBlockSize, (uint)(chunk.Data.Length - pos));
                    blocks.Add((cas.Position, (uint)size));
                    cas.Write(chunk.Data, pos, size);
                    // Motor blokları AES blok boyutuna (16) yuvarlanmış uzunlukla okur; dosya sonunu aşmamak için hizala
                    int pad = (int)(-cas.Position & 15);
                    if (pad > 0) cas.Write(new byte[pad]);
                }
                if (chunk.Data.Length == 0) blocks.Add((cas.Position, 0)); // boş parça da bir blok adresi tutar
            }
        }

        var directoryIndex = BuildDirectoryIndex(mountPoint, all);

        using var toc = new FileStream(basePath + ".utoc", FileMode.Create, FileAccess.Write, FileShare.None);
        using var w = new BinaryWriter(toc, Encoding.UTF8, leaveOpen: true);

        // FIoStoreTocHeader (144 bayt)
        w.Write(TocMagic);
        w.Write((byte)tocVersion);
        w.Write((byte)0);           // reserved0
        w.Write((ushort)0);         // reserved1
        w.Write((uint)TocHeaderSize);
        w.Write((uint)all.Count);   // TocEntryCount
        w.Write((uint)blocks.Count);
        w.Write(12u);               // TocCompressedBlockEntrySize
        w.Write(0u);                // CompressionMethodNameCount (sıkıştırma yok)
        w.Write((uint)CompressionMethodNameLength);
        w.Write(compressionBlockSize);
        w.Write((uint)directoryIndex.Length);
        w.Write(1u);                // PartitionCount
        w.Write(containerId);
        w.Write(new byte[16]);      // EncryptionKeyGuid
        w.Write((byte)EIoContainerFlags.Indexed);
        w.Write((byte)0);           // reserved3 / EncryptionMethod = None
        w.Write((ushort)0);         // reserved4
        w.Write(0u);                // TocChunkPerfectHashSeedsCount → motor sıralı yedek eşlemeyi kullanır
        w.Write(ulong.MaxValue);    // PartitionSize
        w.Write(0u);                // TocChunksWithoutPerfectHashCount
        w.Write(0u);                // reserved7
        for (int i = 0; i < 5; i++) w.Write(0UL); // reserved8
        if (toc.Position != TocHeaderSize) throw new InvalidOperationException("TOC başlık boyutu hatalı.");

        // Parça kimlikleri
        foreach (var c in all)
        {
            w.Write(c.Id);
            w.Write((ushort)((c.Index & 0xFF) << 8 | (c.Index & 0xFF00) >> 8)); // NETWORK_ORDER16
            w.Write((byte)0);
            w.Write((byte)c.Type);
        }

        // Parça ofset/uzunlukları (5 + 5 bayt, büyük uçlu)
        Span<byte> ol = stackalloc byte[10];
        foreach (var (offset, length) in offsetLengths)
        {
            WriteUInt40BigEndian(ol[..5], offset);
            WriteUInt40BigEndian(ol[5..], length);
            w.Write(ol);
        }

        // Sıkıştırma blokları: 40 bit ofset + 24 bit sıkıştırılmış boyut, 24 bit açık boyut + 8 bit yöntem (0 = yok)
        foreach (var (casOffset, size) in blocks)
        {
            w.Write((ulong)casOffset | (ulong)size << 40);
            w.Write(size & 0xFFFFFF);
        }

        // Dizin indeksi
        w.Write(directoryIndex);

        // Parça meta verisi: hash + bayraklar
        bool ioHash = tocVersion >= EIoStoreTocVersion.ReplaceIoChunkHashWithIoHash;
        Span<byte> hash = stackalloc byte[32];
        foreach (var c in all)
        {
            if (ioHash)
            {
                // FIoHash: BLAKE3-160
                Blake3.Hasher.Hash(c.Data, hash);
                w.Write(hash[..20]);
                w.Write((byte)0);       // flags
                w.Write(new byte[3]);   // dolgu
            }
            else
            {
                // FIoChunkHash: SHA1 (20 bayt) + 12 bayt sıfır
                w.Write(SHA1.HashData(c.Data));
                w.Write(new byte[12]);
                w.Write((byte)0);
            }
        }
    }

    /// <summary><c>FIoContainerId::FromName</c>: küçük harfe çevrilmiş adın UTF-16 baytlarının CityHash64'ü.</summary>
    public static ulong ContainerIdFromName(string name)
        => CityHash.CityHash64(Encoding.Unicode.GetBytes(name.ToLowerInvariant()));

    private static void WriteUInt40BigEndian(Span<byte> dest, ulong value)
    {
        if (value > 0xFF_FFFF_FFFFUL) throw new InvalidOperationException("Konteyner 1 TB sınırını aşıyor.");
        for (int i = 4; i >= 0; i--)
        {
            dest[i] = (byte)value;
            value >>= 8;
        }
    }

    /// <summary>FIoContainerHeader: paket kimlikleri ve paket deposu girdileri (sürüme göre).</summary>
    internal static byte[] BuildContainerHeader(ulong containerId, IReadOnlyList<IoStoreChunk> packageChunks, EIoContainerHeaderVersion version)
    {
        if (version < EIoContainerHeaderVersion.Initial)
            throw new NotSupportedException($"Konteyner başlığı sürümü {version} yazılamıyor (UE 5.0+ gerekir).");

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(ContainerHeaderSignature);
        w.Write((uint)version);
        w.Write(containerId);
        if (version < EIoContainerHeaderVersion.OptionalSegmentPackages)
            w.Write((uint)packageChunks.Count);

        // PackageIds
        w.Write(packageChunks.Count);
        foreach (var c in packageChunks) w.Write(c.Package!.PackageId);

        // StoreEntries: sabit boyutlu girdiler + ardından içe aktarılan paket dizileri (FFilePackageStoreEntry, CArrayView ofsetleri girdiye göreli)
        var store = BuildStoreEntries(packageChunks.Select(c => c.Package!).ToList(), version);
        w.Write(store.Length);
        w.Write(store);

        if (version >= EIoContainerHeaderVersion.OptionalSegmentPackages)
        {
            w.Write(0);      // OptionalSegmentPackageIds
            w.Write(0);      // OptionalSegmentStoreEntries boyutu
        }

        w.Write(0u);         // RedirectsNameMap (ad toplu yüklemesi: 0 ad)
        if (version >= EIoContainerHeaderVersion.LocalizedPackages)
            w.Write(0);      // LocalizedPackages
        w.Write(0);          // PackageRedirects

        if (version == EIoContainerHeaderVersion.SoftPackageReferences)
        {
            w.Write(0);      // bContainsSoftPackageReferences = false (UBOOL)
        }
        else if (version >= EIoContainerHeaderVersion.SoftPackageReferencesOffset)
        {
            w.Write(0L);     // SoftPackageReferencesSerialInfo.Offset
            w.Write(0L);     // SoftPackageReferencesSerialInfo.Size
        }

        w.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildStoreEntries(IReadOnlyList<IoStorePackageInfo> packages, EIoContainerHeaderVersion version)
    {
        bool hasExportInfo = version < EIoContainerHeaderVersion.NoExportInfo;
        int entrySize = (hasExportInfo ? 8 : 0) + 8 + 8; // [ExportCount, ExportBundleCount] + ImportedPackages view + ShaderMapHashes view
        int fixedSize = entrySize * packages.Count;
        var buffer = new byte[fixedSize + packages.Sum(p => p.ImportedPackages.Count * 8)];
        var span = buffer.AsSpan();
        int dataPos = fixedSize;

        for (int i = 0; i < packages.Count; i++)
        {
            var p = packages[i];
            int pos = i * entrySize;
            if (hasExportInfo)
            {
                BinaryPrimitives.WriteInt32LittleEndian(span[pos..], p.ExportCount);
                BinaryPrimitives.WriteInt32LittleEndian(span[(pos + 4)..], p.ExportBundleCount);
                pos += 8;
            }

            // TFilePackageStoreEntryCArrayView: ArrayNum + OffsetToDataFromThis
            BinaryPrimitives.WriteInt32LittleEndian(span[pos..], p.ImportedPackages.Count);
            BinaryPrimitives.WriteInt32LittleEndian(span[(pos + 4)..], p.ImportedPackages.Count == 0 ? 0 : dataPos - pos);
            foreach (var imported in p.ImportedPackages)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(span[dataPos..], imported);
                dataPos += 8;
            }
            // ShaderMapHashes: boş (pişirilmiş shader eşlemeleri değiştirilmez)
        }
        return buffer;
    }

    /// <summary>
    /// FIoDirectoryIndexResource: bağlama noktası, dizin ağacı, dosya girdileri (UserData = TOC girdi dizini) ve dize tablosu.
    /// </summary>
    internal static byte[] BuildDirectoryIndex(string mountPoint, IReadOnlyList<IoStoreChunk> chunks)
    {
        var strings = new List<string>();
        var stringIndex = new Dictionary<string, uint>(StringComparer.Ordinal);
        uint Intern(string s)
        {
            if (!stringIndex.TryGetValue(s, out var i))
            {
                i = (uint)strings.Count;
                stringIndex[s] = i;
                strings.Add(s);
            }
            return i;
        }

        var dirs = new List<DirNode> { new() { Name = InvalidHandle } }; // kök
        var files = new List<(uint Name, uint Next, uint UserData)>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var path = chunks[i].Path;
            if (string.IsNullOrEmpty(path)) continue;
            var parts = path.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            int dir = 0;
            for (int k = 0; k < parts.Length - 1; k++)
                dir = GetOrAddChild(dirs, dir, Intern(parts[k]));

            // Dosyayı dizinin dosya listesinin başına ekle
            files.Add((Intern(parts[^1]), dirs[dir].FirstFile, (uint)i));
            dirs[dir].FirstFile = (uint)(files.Count - 1);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, mountPoint);
        w.Write(dirs.Count);
        foreach (var d in dirs)
        {
            w.Write(d.Name);
            w.Write(d.FirstChild);
            w.Write(d.NextSibling);
            w.Write(d.FirstFile);
        }
        w.Write(files.Count);
        foreach (var (name, next, userData) in files)
        {
            w.Write(name);
            w.Write(next);
            w.Write(userData);
        }
        w.Write(strings.Count);
        foreach (var s in strings) WriteFString(w, s);
        w.Flush();
        return ms.ToArray();
    }

    private static int GetOrAddChild(List<DirNode> dirs, int parent, uint name)
    {
        for (uint c = dirs[parent].FirstChild; c != InvalidHandle; c = dirs[(int)c].NextSibling)
            if (dirs[(int)c].Name == name) return (int)c;

        dirs.Add(new DirNode { Name = name, NextSibling = dirs[parent].FirstChild });
        dirs[parent].FirstChild = (uint)(dirs.Count - 1);
        return dirs.Count - 1;
    }

    private static void WriteFString(BinaryWriter w, string s)
    {
        if (s.Length == 0)
        {
            w.Write(0);
            return;
        }
        bool ascii = s.All(c => c < 0x80);
        if (ascii)
        {
            w.Write(s.Length + 1);
            w.Write(Encoding.ASCII.GetBytes(s));
            w.Write((byte)0);
        }
        else
        {
            w.Write(-(s.Length + 1));
            w.Write(Encoding.Unicode.GetBytes(s));
            w.Write((ushort)0);
        }
    }

    private sealed class DirNode
    {
        public uint Name;
        public uint FirstChild = InvalidHandle;
        public uint NextSibling = InvalidHandle;
        public uint FirstFile = InvalidHandle;
    }
}
