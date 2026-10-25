using System.Buffers.Binary;
using System.Text;

namespace LocalizePak.IO.Formats.Unity;

/// <summary>Unity TypeTree düğümü (bir alanın türü, adı, boyutu ve hizalama bayrağı).</summary>
public sealed class UnityTypeTreeNode
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public int ByteSize { get; init; }
    public int Level { get; init; }
    public int TypeFlags { get; init; }
    public int MetaFlag { get; init; }
    public List<UnityTypeTreeNode> Children { get; } = [];

    public bool IsArray => (TypeFlags & 1) != 0 || Type == "Array";
    public bool AlignAfter => (MetaFlag & 0x4000) != 0;

    /// <summary>Düz (seviye numaralı) düğüm listesinden ağaç kurar; kök düğümü döndürür.</summary>
    public static UnityTypeTreeNode BuildTree(IReadOnlyList<UnityTypeTreeNode> flat)
    {
        if (flat.Count == 0) throw new InvalidDataException("Boş TypeTree.");
        var stack = new Stack<UnityTypeTreeNode>();
        var root = flat[0];
        stack.Push(root);
        for (int i = 1; i < flat.Count; i++)
        {
            var n = flat[i];
            while (stack.Count > 0 && stack.Peek().Level >= n.Level) stack.Pop();
            if (stack.Count == 0) throw new InvalidDataException("Bozuk TypeTree seviye yapısı.");
            stack.Peek().Children.Add(n);
            stack.Push(n);
        }
        return root;
    }

    public static UnityTypeTreeNode Synthetic(string type, string name, int byteSize, int metaFlag, params UnityTypeTreeNode[] children)
    {
        var n = new UnityTypeTreeNode { Type = type, Name = name, ByteSize = byteSize, MetaFlag = metaFlag };
        n.Children.AddRange(children);
        return n;
    }

    /// <summary>TypeTree'si olmayan dosyalar için TextAsset düzeni: m_Name, m_Script (her ikisi hizalı string).</summary>
    public static UnityTypeTreeNode TextAssetLayout(bool withPathName)
    {
        var root = Synthetic("TextAsset", "Base", -1, 0,
            Synthetic("string", "m_Name", -1, 0x4000),
            Synthetic("string", "m_Script", -1, 0x4000));
        if (withPathName) root.Children.Add(Synthetic("string", "m_PathName", -1, 0x4000));
        return root;
    }
}

/// <summary>Nesne verisinde bulunan bir metin alanı.</summary>
public sealed record UnityStringField(string Path, string Value);

/// <summary>
/// Bir nesnenin ikili verisini TypeTree'ye göre gezer: tüm metin alanlarını toplar ve istenirse
/// verilen alanları değiştirerek nesneyi Unity'nin hizalama kurallarıyla yeniden serileştirir.
/// Veri ile TypeTree tam örtüşmezse (kalan bayt / taşma) hata fırlatır — böylece bozuk çıktı üretilmez.
/// </summary>
public sealed class UnityTypeTreeWalker
{
    private readonly byte[] _data;
    private readonly bool _bigEndian;
    private readonly IReadOnlyDictionary<string, string>? _replacements;
    private readonly MemoryStream? _output;
    private readonly Func<string, string, string, UnityTypeTreeNode?>? _refTypeResolver;
    private readonly List<UnityStringField> _strings = [];
    private int _pos;
    private int _replacedCount;

    private const int MaxStringBytes = 64 * 1024 * 1024;
    private const int MaxArrayCount = 50_000_000;

    private UnityTypeTreeWalker(byte[] data, bool bigEndian, IReadOnlyDictionary<string, string>? replacements, bool write,
        Func<string, string, string, UnityTypeTreeNode?>? refTypeResolver)
    {
        _data = data;
        _bigEndian = bigEndian;
        _replacements = replacements;
        _output = write ? new MemoryStream(data.Length + 256) : null;
        _refTypeResolver = refTypeResolver;
    }

    /// <summary>Nesnedeki tüm metin alanlarını yol ("m_Items[3].text") ile döndürür.</summary>
    /// <param name="refTypeResolver">[SerializeReference] tiplerini (sınıf, ad alanı, derleme) TypeTree'ye çözen fonksiyon.</param>
    public static List<UnityStringField> ReadStrings(UnityTypeTreeNode root, byte[] data, bool bigEndian,
        Func<string, string, string, UnityTypeTreeNode?>? refTypeResolver = null)
    {
        var w = new UnityTypeTreeWalker(data, bigEndian, null, write: false, refTypeResolver);
        w.Walk(root, string.Empty);
        w.EnsureFullyConsumed();
        return w._strings;
    }

    /// <summary>Verilen yollardaki metinleri değiştirerek nesneyi yeniden serileştirir.</summary>
    public static byte[] Rewrite(UnityTypeTreeNode root, byte[] data, bool bigEndian, IReadOnlyDictionary<string, string> replacements, out int replacedCount,
        Func<string, string, string, UnityTypeTreeNode?>? refTypeResolver = null)
    {
        var w = new UnityTypeTreeWalker(data, bigEndian, replacements, write: true, refTypeResolver);
        w.Walk(root, string.Empty);
        w.EnsureFullyConsumed();
        replacedCount = w._replacedCount;
        return w._output!.ToArray();
    }

    private void EnsureFullyConsumed()
    {
        if (_pos != _data.Length)
            throw new InvalidDataException($"TypeTree nesne verisiyle örtüşmüyor ({_pos}/{_data.Length} bayt okundu).");
    }

    private void Walk(UnityTypeTreeNode node, string path)
    {
        switch (node.Type)
        {
            case "bool": case "char": case "SInt8": case "UInt8":
                Copy(1); break;
            case "SInt16": case "UInt16": case "short": case "unsigned short":
                Copy(2); break;
            case "int": case "SInt32": case "UInt32": case "unsigned int": case "float": case "Type*":
                Copy(4); break;
            case "long long": case "SInt64": case "UInt64": case "unsigned long long": case "double": case "FileSize":
                Copy(8); break;
            case "string":
                WalkString(node, path);
                break;
            case "TypelessData":
                {
                    int size = ReadInt32Copy();
                    if (size < 0 || size > _data.Length - _pos) throw new InvalidDataException($"Geçersiz TypelessData boyutu: {size}");
                    Copy(size);
                    break;
                }
            case "ReferencedObject":
                WalkReferencedObject(node, path);
                break;
            default:
                if (node.IsArray)
                {
                    WalkArray(node, path);
                }
                else if (node.Children.Count == 1 && node.Children[0].IsArray)
                {
                    WalkArray(node.Children[0], path);
                    if (node.Children[0].AlignAfter) Align();
                }
                else if (node.Children.Count == 0 && node.ByteSize > 0)
                {
                    Copy(node.ByteSize); // bilinmeyen sabit boyutlu ilkel tür
                }
                else
                {
                    foreach (var child in node.Children)
                        Walk(child, path.Length == 0 ? child.Name : $"{path}.{child.Name}");
                }
                break;
        }

        if (node.AlignAfter) Align();
    }

    /// <summary>
    /// [SerializeReference] kaydı: rid, type{class, ns, asm}, data. "data"nın düzeni nesnenin kendi TypeTree'sinde
    /// yoktur; dosyanın refTypes listesinden (sınıf + ad alanı + derleme) çözülen TypeTree ile okunur.
    /// </summary>
    private void WalkReferencedObject(UnityTypeTreeNode node, string path)
    {
        string? cls = null, ns = null, asm = null;
        foreach (var child in node.Children)
        {
            var childPath = $"{path}.{child.Name}";
            if (child.Type == "ReferencedObjectData")
            {
                if (!string.IsNullOrEmpty(cls))
                {
                    var refTree = _refTypeResolver?.Invoke(cls, ns ?? string.Empty, asm ?? string.Empty)
                        ?? throw new InvalidDataException($"SerializeReference tipi çözülemedi: {ns}.{cls} ({asm})");
                    Walk(refTree, childPath);
                }
                if (child.AlignAfter) Align();
                continue;
            }

            int before = _strings.Count;
            Walk(child, childPath);
            if (child.Type == "ReferencedManagedType" && _strings.Count - before >= 3)
            {
                cls = _strings[before].Value;
                ns = _strings[before + 1].Value;
                asm = _strings[before + 2].Value;
            }
        }
    }

    private void WalkArray(UnityTypeTreeNode arrayNode, string path)
    {
        if (arrayNode.Children.Count < 2) throw new InvalidDataException("Geçersiz TypeTree dizi düğümü.");
        int count = ReadInt32Copy();
        if (count < 0 || count > MaxArrayCount) throw new InvalidDataException($"Geçersiz dizi uzunluğu: {count}");

        var element = arrayNode.Children[1];
        if (element.Children.Count == 0 && element.ByteSize > 0 && element.Type != "string")
        {
            long bytes = (long)count * element.ByteSize;
            if (bytes > _data.Length - _pos) throw new InvalidDataException("Dizi verisi nesne sınırlarını aşıyor.");
            Copy((int)bytes); // ilkel dizi (byte[], int[] ...) — tek seferde
        }
        else
        {
            for (int i = 0; i < count; i++)
                Walk(element, $"{path}[{i}]");
        }
    }

    private void WalkString(UnityTypeTreeNode node, string path)
    {
        int len = ReadInt32();
        if (len < 0 || len > MaxStringBytes || len > _data.Length - _pos)
            throw new InvalidDataException($"Geçersiz metin uzunluğu ({len}) — '{path}'");
        var value = Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        _strings.Add(new UnityStringField(path, value));

        if (_output != null)
        {
            byte[] bytes;
            if (_replacements != null && _replacements.TryGetValue(path, out var replacement) && replacement != value)
            {
                bytes = Encoding.UTF8.GetBytes(replacement);
                _replacedCount++;
            }
            else
            {
                bytes = _data.AsSpan(_pos - len, len).ToArray();
            }
            WriteInt32(bytes.Length);
            _output.Write(bytes);
        }

        // string düğümünün hizalaması kendisinde veya "Array" alt düğümünde işaretlidir
        if (!node.AlignAfter && node.Children.Count > 0 && node.Children[0].AlignAfter) Align();
    }

    private void Copy(int count)
    {
        if (count < 0 || count > _data.Length - _pos) throw new InvalidDataException("Nesne verisi beklenenden kısa.");
        _output?.Write(_data, _pos, count);
        _pos += count;
    }

    private int ReadInt32()
    {
        if (_data.Length - _pos < 4) throw new InvalidDataException("Nesne verisi beklenenden kısa.");
        int v = _bigEndian ? BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_pos)) : BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    private int ReadInt32Copy()
    {
        int v = ReadInt32();
        if (_output != null) WriteInt32(v);
        return v;
    }

    private void WriteInt32(int v)
    {
        Span<byte> b = stackalloc byte[4];
        if (_bigEndian) BinaryPrimitives.WriteInt32BigEndian(b, v); else BinaryPrimitives.WriteInt32LittleEndian(b, v);
        _output!.Write(b);
    }

    private void Align()
    {
        int pad = (4 - _pos % 4) % 4;
        if (pad > _data.Length - _pos) throw new InvalidDataException("Hizalama nesne sonunu aşıyor.");
        _pos += pad;
        if (_output != null)
        {
            long outPad = (4 - _output.Length % 4) % 4;
            for (int i = 0; i < outPad; i++) _output.WriteByte(0);
        }
    }
}

/// <summary>
/// Unity'nin TypeTree "ortak metin" tablosu: tür/alan adı ofsetinin en üst biti (0x80000000) setliyse ad bu tablodan okunur.
/// Sıra Unity'deki CommonString.h ile aynıdır; ofsetler ardışık null-sonlu dizilerden hesaplanır.
/// </summary>
public static class UnityCommonStrings
{
    private static readonly string[] Ordered =
    [
        "AABB", "AnimationClip", "AnimationCurve", "AnimationState", "Array", "Base", "BitField", "bitset", "bool", "char",
        "ColorRGBA", "Component", "data", "deque", "double", "dynamic_array", "FastPropertyName", "first", "float", "Font",
        "GameObject", "Generic Mono", "GradientNEW", "GUID", "GUIStyle", "int", "list", "long long", "map", "Matrix4x4f",
        "MdFour", "MonoBehaviour", "MonoScript", "m_ByteSize", "m_Curve", "m_EditorClassIdentifier", "m_EditorHideFlags",
        "m_Enabled", "m_ExtensionPtr", "m_GameObject", "m_Index", "m_IsArray", "m_IsStatic", "m_MetaFlag", "m_Name",
        "m_ObjectHideFlags", "m_PrefabInternal", "m_PrefabParentObject", "m_Script", "m_StaticEditorFlags", "m_Type",
        "m_Version", "Object", "pair", "PPtr<Component>", "PPtr<GameObject>", "PPtr<Material>", "PPtr<MonoBehaviour>",
        "PPtr<MonoScript>", "PPtr<Object>", "PPtr<Prefab>", "PPtr<Sprite>", "PPtr<TextAsset>", "PPtr<Texture>",
        "PPtr<Texture2D>", "PPtr<Transform>", "Prefab", "Quaternionf", "Rectf", "RectInt", "RectOffset", "second", "set",
        "short", "size", "SInt16", "SInt32", "SInt64", "SInt8", "staticvector", "string", "TextAsset", "TextMesh", "Texture",
        "Texture2D", "Transform", "TypelessData", "UInt16", "UInt32", "UInt64", "UInt8", "unsigned int", "unsigned long long",
        "unsigned short", "vector", "Vector2f", "Vector3f", "Vector4f", "m_ScriptingClassIdentifier", "Gradient", "Type*",
        "int2_storage", "int3_storage", "BoundsInt", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
        "FileSize", "Hash128", "RenderingLayerMask",
    ];

    private static readonly Dictionary<uint, string> ByOffset = Build();

    private static Dictionary<uint, string> Build()
    {
        var map = new Dictionary<uint, string>();
        uint offset = 0;
        foreach (var s in Ordered)
        {
            map[offset] = s;
            offset += (uint)Encoding.ASCII.GetByteCount(s) + 1;
        }
        return map;
    }

    public static bool TryGet(uint offset, out string value) => ByOffset.TryGetValue(offset, out value!);

    /// <summary>Test/teşhis için: verilen metnin ortak tablodaki ofseti (yoksa -1).</summary>
    public static long OffsetOf(string value)
    {
        foreach (var (offset, text) in ByOffset)
            if (text == value) return offset;
        return -1;
    }
}
