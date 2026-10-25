using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using LocalizePak.Core.Enums;
using LocalizePak.IO;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Formats.Unity;
using Xunit;

namespace LocalizePak.Tests;

/// <summary>
/// Unity TextAsset / MonoBehaviour çıkarıcısı. Gerçek oyun dosyaları telif nedeniyle depoya konamaz;
/// testler Unity biçimine uygun sentetik SerializedFile (v22, TypeTree'li) ve UnityFS paketleri üretir.
/// </summary>
public class UnityTextArchiveTests
{
    // ─── Sentetik Unity dosyası üretici ──────────────────────────────────────

    private const long TextAssetPathId = 100;
    private const long MonoBehaviourPathId = -7_000_000_000L; // 64-bit negatif pathID de desteklenmeli
    private const long OtherPathId = 300;

    private sealed class TreeBuilder
    {
        private readonly List<(int Level, int Flags, string Type, string Name, int Size, int Meta)> _nodes = [];
        public TreeBuilder Add(int level, string type, string name, int size = -1, int meta = 0, int flags = 0)
        {
            _nodes.Add((level, flags, type, name, size, meta));
            return this;
        }

        public TreeBuilder AddString(int level, string name)
        {
            Add(level, "string", name, -1, 0x8000);
            Add(level + 1, "Array", "Array", -1, 0x4001, flags: 1);
            Add(level + 2, "int", "size", 4);
            Add(level + 2, "char", "data", 1);
            return this;
        }

        public byte[] Blob()
        {
            var strings = new MemoryStream();
            var offsets = new Dictionary<string, uint>();
            uint Off(string s)
            {
                if (s == "string") return 0x80000000 | 840; // ortak metin tablosu yolunu da sına
                if (!offsets.TryGetValue(s, out var o))
                {
                    o = (uint)strings.Length;
                    strings.Write(Encoding.UTF8.GetBytes(s));
                    strings.WriteByte(0);
                    offsets[s] = o;
                }
                return o;
            }

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(_nodes.Count);
            var nodeBytes = new MemoryStream();
            var nw = new BinaryWriter(nodeBytes);
            int index = 0;
            foreach (var n in _nodes)
            {
                nw.Write((ushort)1);
                nw.Write((byte)n.Level);
                nw.Write((byte)n.Flags);
                nw.Write(Off(n.Type));
                nw.Write(Off(n.Name));
                nw.Write(n.Size);
                nw.Write(index++);
                nw.Write(n.Meta);
                nw.Write(0UL); // refTypeHash (v19+)
            }
            w.Write((int)strings.Length);
            w.Write(nodeBytes.ToArray());
            w.Write(strings.ToArray());
            return ms.ToArray();
        }
    }

    private static TreeBuilder TextAssetTree() => new TreeBuilder()
        .Add(0, "TextAsset", "Base")
        .AddString(1, "m_Name")
        .AddString(1, "m_Script");

    private static TreeBuilder DialogueTree() => new TreeBuilder()
        .Add(0, "MonoBehaviour", "Base")
        .Add(1, "PPtr<GameObject>", "m_GameObject").Add(2, "int", "m_FileID", 4).Add(2, "SInt64", "m_PathID", 8)
        .Add(1, "UInt8", "m_Enabled", 1, 0x4000)
        .Add(1, "PPtr<MonoScript>", "m_Script").Add(2, "int", "m_FileID", 4).Add(2, "SInt64", "m_PathID", 8)
        .AddString(1, "m_Name")
        .AddString(1, "title")
        .AddString(1, "speakerId")
        .Add(1, "vector", "lines", -1, 0x4000).Add(2, "Array", "Array", -1, 0x4000, flags: 1).Add(3, "int", "size", 4).AddString(3, "data")
        .Add(1, "UInt8", "isFinal", 1, 0x4000)
        .Add(1, "int", "count", 4);

    private static void AlignedString(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write(b.Length);
        w.Write(b);
        while (w.BaseStream.Position % 4 != 0) w.Write((byte)0);
    }

    private static byte[] TextAssetData(string name, string script)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        AlignedString(w, name);
        AlignedString(w, script);
        return ms.ToArray();
    }

    private static byte[] DialogueData(string title, string speakerId, string[] lines)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(0); w.Write(12345L);            // m_GameObject
        w.Write((byte)1); w.Write(new byte[3]); // m_Enabled + hizalama
        w.Write(0); w.Write(678L);              // m_Script
        AlignedString(w, "QuestDialogue");
        AlignedString(w, title);
        AlignedString(w, speakerId);
        w.Write(lines.Length);
        foreach (var l in lines) AlignedString(w, l);
        w.Write((byte)1); w.Write(new byte[3]);
        w.Write(42);
        return ms.ToArray();
    }

    /// <summary>Unity 2022 biçiminde (sürüm 22, little-endian, TypeTree'li) SerializedFile üretir.</summary>
    private static byte[] BuildSerializedFile(byte[]? dialogueOverride = null)
    {
        var objects = new List<(long PathId, int TypeIndex, byte[] Data)>
        {
            (TextAssetPathId, 0, TextAssetData("Dialogue", "key,text\ngreet,Hello traveler\nbye,Farewell")),
            (MonoBehaviourPathId, 1, dialogueOverride ?? DialogueData("The Lost Sword", "npc_blacksmith_01", ["Welcome to my forge!", "Bring me iron ore."])),
            (OtherPathId, 2, [1, 2, 3, 4, 5, 6, 7]) // metin içermeyen başka bir nesne (bayt bayt korunmalı)
        };

        var meta = new MemoryStream();
        var mw = new BinaryWriter(meta);
        meta.Write(new byte[48]); // başlık sonra doldurulur
        mw.Write(Encoding.ASCII.GetBytes("2022.3.0f1\0"));
        mw.Write(19);           // platform
        mw.Write((byte)1);      // enableTypeTree
        mw.Write(3);            // tür sayısı
        void WriteType(int classId, short scriptIndex, byte[]? blob)
        {
            mw.Write(classId);
            mw.Write((byte)0);
            mw.Write(scriptIndex);
            if (classId == 114) mw.Write(new byte[16]);
            mw.Write(new byte[16]);
            mw.Write(blob ?? new TreeBuilder().Add(0, "Mesh", "Base", 7).Blob());
            mw.Write(0); // bağımlılık sayısı (v21+)
        }
        WriteType(49, -1, TextAssetTree().Blob());
        WriteType(114, 0, DialogueTree().Blob());
        WriteType(43, -1, null);

        mw.Write(objects.Count);
        var tableOffsets = new List<long>();
        foreach (var o in objects)
        {
            while (meta.Position % 4 != 0) mw.Write((byte)0);
            mw.Write(o.PathId);
            tableOffsets.Add(meta.Position);
            mw.Write(0L);           // byteStart (sonra)
            mw.Write((uint)o.Data.Length);
            mw.Write(o.TypeIndex);
        }
        mw.Write(0); // script tipleri
        mw.Write(0); // harici dosyalar
        mw.Write(0); // ref tipleri
        mw.Write((byte)0); // userInformation

        while (meta.Position % 16 != 0) mw.Write((byte)0);
        long dataOffset = meta.Position;
        var data = new MemoryStream();
        for (int i = 0; i < objects.Count; i++)
        {
            while (data.Position % 8 != 0) data.WriteByte(0);
            long start = data.Position;
            data.Write(objects[i].Data);
            meta.Position = tableOffsets[i];
            mw.Write(start);
        }

        var file = meta.ToArray().Concat(data.ToArray()).ToArray();
        var h = file.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(h[8..], 22);
        BinaryPrimitives.WriteInt64BigEndian(h[24..], file.Length);
        BinaryPrimitives.WriteInt64BigEndian(h[32..], dataOffset);
        return file;
    }

    /// <summary>Tek düğümlü, sıkıştırılmamış UnityFS (sürüm 8) paketi üretir.</summary>
    private static byte[] BuildBundle(byte[] serializedFile)
    {
        var info = new MemoryStream();
        var tmp = new byte[8];
        info.Write(new byte[16]);
        BinaryPrimitives.WriteInt32BigEndian(tmp, 1); info.Write(tmp, 0, 4);
        BinaryPrimitives.WriteUInt32BigEndian(tmp, (uint)serializedFile.Length); info.Write(tmp, 0, 4);
        BinaryPrimitives.WriteUInt32BigEndian(tmp, (uint)serializedFile.Length); info.Write(tmp, 0, 4);
        BinaryPrimitives.WriteUInt16BigEndian(tmp, 0); info.Write(tmp, 0, 2);
        BinaryPrimitives.WriteInt32BigEndian(tmp, 1); info.Write(tmp, 0, 4);
        BinaryPrimitives.WriteInt64BigEndian(tmp, 0); info.Write(tmp, 0, 8);
        BinaryPrimitives.WriteInt64BigEndian(tmp, serializedFile.Length); info.Write(tmp, 0, 8);
        BinaryPrimitives.WriteUInt32BigEndian(tmp, 4); info.Write(tmp, 0, 4);
        info.Write(Encoding.ASCII.GetBytes("CAB-synthetic\0"));
        var infoBytes = info.ToArray();

        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("UnityFS\0"));
        BinaryPrimitives.WriteUInt32BigEndian(tmp, 8); ms.Write(tmp, 0, 4);
        ms.Write(Encoding.ASCII.GetBytes("5.x.x\0"));
        ms.Write(Encoding.ASCII.GetBytes("2022.3.0f1\0"));
        long sizePos = ms.Position;
        ms.Write(new byte[8]);
        BinaryPrimitives.WriteUInt32BigEndian(tmp, (uint)infoBytes.Length); ms.Write(tmp, 0, 4);
        BinaryPrimitives.WriteUInt32BigEndian(tmp, (uint)infoBytes.Length); ms.Write(tmp, 0, 4);
        BinaryPrimitives.WriteUInt32BigEndian(tmp, 0x40 | 0x200); ms.Write(tmp, 0, 4);
        while (ms.Position % 16 != 0) ms.WriteByte(0);
        ms.Write(infoBytes);
        while (ms.Position % 16 != 0) ms.WriteByte(0);
        ms.Write(serializedFile);
        var bytes = ms.ToArray();
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan((int)sizePos), bytes.Length);
        return bytes;
    }

    private static string TempFile(string name, byte[] content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "gls-unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    // ─── Testler ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AABB", 0)]
    [InlineData("Array", 49)]
    [InlineData("MonoBehaviour", 263)]
    [InlineData("m_Name", 427)]
    [InlineData("string", 840)]
    [InlineData("TypelessData", 894)]
    [InlineData("vector", 981)]
    [InlineData("RenderingLayerMask", 1169)]
    public void CommonStrings_MatchUnityOffsets(string text, long offset)
    {
        Assert.Equal(offset, UnityCommonStrings.OffsetOf(text));
    }

    [Fact]
    public void SerializedFile_ListsTextAssetAndTranslatableMonoBehaviourFields()
    {
        var path = TempFile("resources.assets", BuildSerializedFile());
        using var archive = UnityTextArchive.Open(path);

        Assert.Equal(2, archive.Items.Count);
        var textAsset = Assert.Single(archive.Items, i => i.Kind == UnityTextKind.TextAsset);
        Assert.Equal($"resources.assets/TextAsset/Dialogue#{TextAssetPathId}.csv", textAsset.VirtualPath);
        Assert.Equal("key,text\ngreet,Hello traveler\nbye,Farewell", Encoding.UTF8.GetString(archive.ReadItem(textAsset)));

        var mb = Assert.Single(archive.Items, i => i.Kind == UnityTextKind.MonoBehaviour);
        var json = JsonNode.Parse(archive.ReadItem(mb))!.AsObject();
        Assert.Equal("The Lost Sword", json["title"]!.GetValue<string>());
        Assert.Equal("Welcome to my forge!", json["lines[0]"]!.GetValue<string>());
        Assert.Equal("Bring me iron ore.", json["lines[1]"]!.GetValue<string>());
        Assert.Null(json["speakerId"]); // kimlik alanı çeviriye açılmamalı
        Assert.Null(json["m_Name"]);
    }

    [Fact]
    public void SerializedFile_SaveChangesOnlyEditedObjects()
    {
        var src = TempFile("resources.assets", BuildSerializedFile());
        var dst = Path.Combine(Path.GetDirectoryName(src)!, "out.assets");
        using (var archive = UnityTextArchive.Open(src))
        {
            var mb = archive.Items.First(i => i.Kind == UnityTextKind.MonoBehaviour);
            var json = JsonNode.Parse(archive.ReadItem(mb))!.AsObject();
            json["title"] = "Kayıp Kılıç — çok daha uzun bir Türkçe başlık";
            json["lines[1]"] = "Bana demir getir.";
            json["speakerId"] = "SALDIRGAN_DEĞİŞİKLİK"; // listede olmayan alan yok sayılmalı

            var ta = archive.Items.First(i => i.Kind == UnityTextKind.TextAsset);
            var changed = archive.Save(dst,
            [
                (mb.VirtualPath, Encoding.UTF8.GetBytes(json.ToJsonString())),
                (ta.VirtualPath, Encoding.UTF8.GetBytes("key,text\ngreet,Merhaba yolcu\nbye,Hoşça kal"))
            ]);
            Assert.Equal(2, changed);
        }

        var expectedDialogue = DialogueData("Kayıp Kılıç — çok daha uzun bir Türkçe başlık", "npc_blacksmith_01", ["Welcome to my forge!", "Bana demir getir."]);
        var x = new UnitySerializedFile(File.OpenRead(src));
        var y = new UnitySerializedFile(File.OpenRead(dst));
        Assert.Equal(x.Objects.Count, y.Objects.Count);

        var other = y.Objects.First(o => o.PathId == OtherPathId);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, y.ReadObjectData(other));
        Assert.Equal(expectedDialogue, y.ReadObjectData(y.Objects.First(o => o.PathId == MonoBehaviourPathId)));
        Assert.Equal(TextAssetData("Dialogue", "key,text\ngreet,Merhaba yolcu\nbye,Hoşça kal"), y.ReadObjectData(y.Objects.First(o => o.PathId == TextAssetPathId)));
        Assert.All(y.Objects, o => Assert.Equal(0, (y.DataOffset + o.ByteStart) % 8));
        Assert.Equal(new FileInfo(dst).Length, y.FileSize);
    }

    [Fact]
    public void Bundle_RoundTrip_RecompressesAndPatchesNode()
    {
        var src = TempFile("dialogue.bundle", BuildBundle(BuildSerializedFile()));
        var dst = Path.Combine(Path.GetDirectoryName(src)!, "out.bundle");
        using (var archive = UnityTextArchive.Open(src))
        {
            Assert.True(archive.IsBundle);
            var mb = archive.Items.First(i => i.Kind == UnityTextKind.MonoBehaviour);
            Assert.StartsWith("CAB-synthetic/MonoBehaviour/QuestDialogue#", mb.VirtualPath);
            var json = JsonNode.Parse(archive.ReadItem(mb))!.AsObject();
            json["lines[0]"] = "Demirhaneme hoş geldin!";
            Assert.Equal(1, archive.Save(dst, [(mb.VirtualPath, Encoding.UTF8.GetBytes(json.ToJsonString()))]));
        }

        using var reopened = UnityTextArchive.Open(dst);
        var item = reopened.Items.First(i => i.Kind == UnityTextKind.MonoBehaviour);
        var after = JsonNode.Parse(reopened.ReadItem(item))!.AsObject();
        Assert.Equal("Demirhaneme hoş geldin!", after["lines[0]"]!.GetValue<string>());
        Assert.Equal("The Lost Sword", after["title"]!.GetValue<string>());

        using var bundle = UnityBundle.Open(dst);
        Assert.Equal(8u, bundle.FormatVersion);
        Assert.Equal("2022.3.0f1", bundle.UnityRevision);
    }

    [Fact]
    public void MonoBehaviour_WithDataNotMatchingTypeTree_IsSkippedNotCorrupted()
    {
        var corrupt = DialogueData("Title", "id", ["a b"]).Concat(new byte[] { 9, 9, 9, 9 }).ToArray();
        var path = TempFile("resources.assets", BuildSerializedFile(corrupt));
        using var archive = UnityTextArchive.Open(path);

        Assert.DoesNotContain(archive.Items, i => i.Kind == UnityTextKind.MonoBehaviour);
        Assert.Equal(1, archive.SkippedMonoBehaviours);
    }

    [Fact]
    public void Save_WithoutChanges_KeepsAllObjectsIdentical()
    {
        var src = TempFile("resources.assets", BuildSerializedFile());
        var dst = Path.Combine(Path.GetDirectoryName(src)!, "same.assets");
        using (var archive = UnityTextArchive.Open(src))
        {
            var items = archive.Items.Select(i => (i.VirtualPath, archive.ReadItem(i))).ToList();
            Assert.Equal(0, archive.Save(dst, items));
        }
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(dst));
    }

    [Fact]
    public async Task PakService_OpensAndPatchesUnityFile_InPlace()
    {
        var path = TempFile("sharedassets0.assets", BuildSerializedFile());
        var svc = new PakService();

        var archive = await svc.OpenAsync(path);
        Assert.Equal(PakFormat.Unity, archive.Format);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(2, archive.LocalizationEntries.Count()); // .csv ve .json girdileri çeviri dosyası olarak görünmeli

        var ta = archive.Entries.First(e => e.VirtualPath.Contains("/TextAsset/"));
        await svc.WriteAsync(path, archive.MountPoint,
            [(ta.VirtualPath, Encoding.UTF8.GetBytes("key,text\ngreet,Selam"))],
            sourceFormat: PakFormat.Unity, sourceArchivePath: path);

        var reopened = await svc.OpenAsync(path);
        using var s = await svc.ExtractEntryAsync(reopened, reopened.Entries.First(e => e.VirtualPath == ta.VirtualPath));
        Assert.Equal("key,text\ngreet,Selam", new StreamReader(s).ReadToEnd());
    }

    [Theory]
    [InlineData("m_TableData[3].m_Localized", "Extraction point activated", true)]
    [InlineData("title", "Start", true)]
    [InlineData("dialogue.lines[2]", "Hello there, traveler!", true)]
    [InlineData("m_Name", "Menu_en-US", false)]
    [InlineData("m_LocaleId.m_Code", "en-US", false)]
    [InlineData("m_Entries[0].m_Key", "MAIN.PRIVATE_GAME", false)]
    [InlineData("speakerId", "npc_01", false)]
    [InlineData("iconPath", "Assets/UI/icon.png", false)]
    [InlineData("description", "UI_START_BUTTON", false)]
    [InlineData("description", "6578110d0d7f6fd47b65a5fbe8aef088", false)]
    [InlineData("references.RefIds[1].type.class", "LocaleSelector", false)]
    [InlineData("label", "12345", false)]
    [InlineData("label", "   ", false)]
    public void IsTranslatableField_FiltersIdentifiers(string path, string value, bool expected)
    {
        Assert.Equal(expected, UnityTextArchive.IsTranslatableField(path, value));
    }

    [Fact]
    public void LooksLikeSerializedFile_RejectsOtherFiles()
    {
        using var text = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 200)));
        using var real = new MemoryStream(BuildSerializedFile());
        Assert.False(UnityTextArchive.CanOpen(text));
        Assert.True(UnityTextArchive.CanOpen(real));
    }
}
