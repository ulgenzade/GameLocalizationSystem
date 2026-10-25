using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LocalizePak.IO.Formats.Unity;

public enum UnityTextKind
{
    TextAsset,
    MonoBehaviour
}

/// <summary>Unity dosyasındaki çevrilebilir metin taşıyan bir nesne (arşiv girdisi olarak gösterilir).</summary>
public sealed class UnityTextItem
{
    public required string VirtualPath { get; init; }
    public required UnityTextKind Kind { get; init; }
    public required string Name { get; init; }
    public required long PathId { get; init; }
    public required int SizeHint { get; init; }
    internal SerializedFileHandle File { get; init; } = null!;
}

/// <summary>Bir SerializedFile ve (paket içindeyse) ait olduğu düğüm.</summary>
internal sealed class SerializedFileHandle(string label, string? nodePath, UnitySerializedFile file)
{
    public string Label { get; } = label;
    public string? NodePath { get; } = nodePath;
    public UnitySerializedFile File { get; } = file;
}

/// <summary>
/// Unity oyun dosyalarındaki (.assets, level*, AssetBundle / UnityFS) metinleri çıkaran ve çeviriyi geri yazan servis.
/// <list type="bullet">
/// <item>TextAsset: ham içerik (JSON / CSV / XML / TXT) olduğu gibi verilir.</item>
/// <item>MonoBehaviour (TypeTree'si olan dosyalarda — Addressables, Unity Localization StringTable vb.): çevrilebilir
/// metin alanları <c>{"alan.yolu": "metin"}</c> JSON'u olarak verilir.</item>
/// </list>
/// Yazarken yalnızca değişen nesneler yeniden serileştirilir; diğer her şey bayt bayt korunur.
/// </summary>
public sealed class UnityTextArchive : IDisposable
{
    private const int MaxTextAssetBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly IReadOnlySet<int> TextClassIds = new HashSet<int> { UnitySerializedFile.ClassIdTextAsset, UnitySerializedFile.ClassIdMonoBehaviour };

    private readonly string _sourcePath;
    private readonly Stream? _fileStream;
    private readonly UnityBundle? _bundle;
    private readonly List<SerializedFileHandle> _files = [];
    private readonly Dictionary<string, UnityTextItem> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<UnityTextItem> Items { get; }
    public bool IsBundle => _bundle != null;
    public string UnityVersion { get; }

    /// <summary>TypeTree'si olmadığı için okunamayan MonoBehaviour sayısı (teşhis amaçlı).</summary>
    public int SkippedMonoBehaviours { get; private set; }

    /// <summary>Akışın UnityFS paketi veya SerializedFile olup olmadığını kontrol eder (konumu korur).</summary>
    public static bool CanOpen(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 48) return false;
        long pos = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> head = stackalloc byte[8];
            stream.ReadExactly(head);
            if (UnityBundle.HasUnityFsSignature(head)) return true;
        }
        finally
        {
            stream.Position = pos;
        }
        return UnitySerializedFile.LooksLikeSerializedFile(stream);
    }

    public static UnityTextArchive Open(string path) => new(path);

    private UnityTextArchive(string path)
    {
        _sourcePath = path;
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        try
        {
            Span<byte> head = stackalloc byte[8];
            fs.ReadExactly(head);
            fs.Position = 0;

            if (UnityBundle.HasUnityFsSignature(head))
            {
                _bundle = new UnityBundle(fs, ownsSource: true);
                UnityVersion = _bundle.UnityRevision;
                foreach (var node in _bundle.Nodes)
                {
                    if (node.Path.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) ||
                        node.Path.EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var ns = _bundle.OpenNode(node);
                    if (!UnitySerializedFile.LooksLikeSerializedFile(ns)) continue;
                    _files.Add(new SerializedFileHandle(node.Path, node.Path, new UnitySerializedFile(ns, TextClassIds)));
                }
            }
            else
            {
                _fileStream = fs;
                var sf = new UnitySerializedFile(fs, TextClassIds);
                UnityVersion = sf.UnityVersion;
                _files.Add(new SerializedFileHandle(System.IO.Path.GetFileName(path), null, sf));
            }
        }
        catch
        {
            fs.Dispose();
            throw;
        }

        var items = new List<UnityTextItem>();
        foreach (var f in _files) items.AddRange(EnumerateItems(f));
        Items = items;
        foreach (var it in items) _byPath[it.VirtualPath] = it;
    }

    // ─── Okuma ───────────────────────────────────────────────────────────────

    private IEnumerable<UnityTextItem> EnumerateItems(SerializedFileHandle handle)
    {
        var sf = handle.File;
        foreach (var obj in sf.Objects)
        {
            if (obj.ClassId == UnitySerializedFile.ClassIdTextAsset)
            {
                if (obj.ByteSize > MaxTextAssetBytes + 4096) continue;
                TextAssetParts parts;
                try { parts = ParseTextAsset(sf.ReadObjectData(obj), sf.BigEndian); }
                catch (InvalidDataException) { continue; }
                if (!IsText(parts.Script, out var text)) continue;

                yield return new UnityTextItem
                {
                    VirtualPath = MakePath(handle, "TextAsset", parts.Name, obj.PathId, SniffExtension(text)),
                    Kind = UnityTextKind.TextAsset,
                    Name = parts.Name,
                    PathId = obj.PathId,
                    SizeHint = parts.Script.Length,
                    File = handle
                };
            }
            else if (obj.ClassId == UnitySerializedFile.ClassIdMonoBehaviour)
            {
                var tree = obj.Type?.Tree;
                if (tree == null)
                {
                    SkippedMonoBehaviours++;
                    continue;
                }

                List<UnityStringField> fields;
                try { fields = UnityTypeTreeWalker.ReadStrings(tree, sf.ReadObjectData(obj), sf.BigEndian, sf.ResolveRefType); }
                catch (InvalidDataException) { SkippedMonoBehaviours++; continue; }

                var translatable = fields.Where(f => IsTranslatableField(f.Path, f.Value)).ToList();
                if (translatable.Count == 0) continue;

                var name = fields.FirstOrDefault(f => f.Path == "m_Name")?.Value ?? string.Empty;
                yield return new UnityTextItem
                {
                    VirtualPath = MakePath(handle, "MonoBehaviour", name, obj.PathId, "json"),
                    Kind = UnityTextKind.MonoBehaviour,
                    Name = name,
                    PathId = obj.PathId,
                    SizeHint = translatable.Sum(f => f.Value.Length),
                    File = handle
                };
            }
        }
    }

    /// <summary>Girdinin içeriği: TextAsset için ham metin, MonoBehaviour için alan→metin JSON'u (UTF-8).</summary>
    public byte[] ReadItem(UnityTextItem item)
    {
        var sf = item.File.File;
        var obj = sf.Objects.First(o => o.PathId == item.PathId);
        var data = sf.ReadObjectData(obj);

        if (item.Kind == UnityTextKind.TextAsset)
            return ParseTextAsset(data, sf.BigEndian).Script;

        var fields = UnityTypeTreeWalker.ReadStrings(obj.Type!.Tree!, data, sf.BigEndian, sf.ResolveRefType);
        var json = new JsonObject();
        foreach (var f in fields.Where(f => IsTranslatableField(f.Path, f.Value)))
            json[f.Path] = f.Value;
        return Encoding.UTF8.GetBytes(json.ToJsonString(JsonOptions));
    }

    public bool TryGetItem(string virtualPath, out UnityTextItem item) => _byPath.TryGetValue(virtualPath, out item!);

    // ─── Yazma ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Verilen girdi içeriklerini (VirtualPath → yeni içerik) uygular ve sonucu <paramref name="outputPath"/>'e yazar.
    /// Değişmeyen veya tanınmayan girdiler yok sayılır.
    /// </summary>
    /// <returns>Değiştirilen nesne sayısı.</returns>
    public int Save(string outputPath, IEnumerable<(string VirtualPath, byte[] Data)> contents)
    {
        var replacementsByFile = new Dictionary<SerializedFileHandle, Dictionary<long, byte[]>>();
        int changed = 0;

        foreach (var (virtualPath, data) in contents)
        {
            if (!_byPath.TryGetValue(virtualPath, out var item)) continue;
            if (ReadItem(item).AsSpan().SequenceEqual(data)) continue;

            var sf = item.File.File;
            var obj = sf.Objects.First(o => o.PathId == item.PathId);
            var original = sf.ReadObjectData(obj);
            byte[] rebuilt;

            if (item.Kind == UnityTextKind.TextAsset)
            {
                rebuilt = RebuildTextAsset(original, sf.BigEndian, data);
            }
            else
            {
                // Yalnızca çeviriye açılan alanlar değiştirilebilir: JSON'a eklenmiş anahtar/kimlik alanları yok sayılır
                var allowed = UnityTypeTreeWalker.ReadStrings(obj.Type!.Tree!, original, sf.BigEndian, sf.ResolveRefType)
                    .Where(f => IsTranslatableField(f.Path, f.Value))
                    .Select(f => f.Path)
                    .ToHashSet(StringComparer.Ordinal);
                var replacements = ParseFieldJson(data)
                    .Where(kv => allowed.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                rebuilt = UnityTypeTreeWalker.Rewrite(obj.Type!.Tree!, original, sf.BigEndian, replacements, out int replaced, sf.ResolveRefType);
                if (replaced == 0) continue;
            }

            if (!replacementsByFile.TryGetValue(item.File, out var reps))
                replacementsByFile[item.File] = reps = [];
            reps[item.PathId] = rebuilt;
            changed++;
        }

        var tempPath = outputPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                if (_bundle != null)
                {
                    var writers = new Dictionary<string, Action<Stream>>();
                    foreach (var (handle, reps) in replacementsByFile)
                    {
                        var node = _bundle.Nodes.First(n => n.Path == handle.NodePath);
                        writers[handle.NodePath!] = s => new UnitySerializedFile(_bundle.OpenNode(node)).WriteTo(s, reps);
                    }
                    _bundle.Write(output, writers);
                }
                else
                {
                    var handle = _files[0];
                    new UnitySerializedFile(_fileStream!).WriteTo(output, replacementsByFile.GetValueOrDefault(handle) ?? []);
                }
            }
            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException) { }
        }

        return changed;
    }

    private static Dictionary<string, string> ParseFieldJson(byte[] data)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (JsonNode.Parse(data) is not JsonObject obj)
            throw new InvalidDataException("MonoBehaviour içeriği bir JSON nesnesi olmalı ({\"alan\": \"metin\"}).");
        foreach (var (key, value) in obj)
        {
            if (value is JsonValue v && v.TryGetValue<string>(out var s))
                result[key] = s;
        }
        return result;
    }

    // ─── TextAsset düzeni: m_Name (hizalı string), m_Script (hizalı string), [kalan alanlar] ───

    private readonly record struct TextAssetParts(string Name, byte[] Script, int ScriptOffset, int ScriptEnd);

    private static TextAssetParts ParseTextAsset(byte[] data, bool bigEndian)
    {
        int pos = 0;
        var nameBytes = ReadAlignedBytes(data, bigEndian, ref pos);
        int scriptOffset = pos;
        var script = ReadAlignedBytes(data, bigEndian, ref pos);
        return new TextAssetParts(Encoding.UTF8.GetString(nameBytes), script, scriptOffset, pos);
    }

    private static byte[] RebuildTextAsset(byte[] original, bool bigEndian, byte[] newScript)
    {
        var parts = ParseTextAsset(original, bigEndian);
        using var ms = new MemoryStream(original.Length + newScript.Length);
        ms.Write(original, 0, parts.ScriptOffset);
        Span<byte> len = stackalloc byte[4];
        if (bigEndian) BinaryPrimitives.WriteInt32BigEndian(len, newScript.Length); else BinaryPrimitives.WriteInt32LittleEndian(len, newScript.Length);
        ms.Write(len);
        ms.Write(newScript);
        while (ms.Length % 4 != 0) ms.WriteByte(0);
        ms.Write(original, parts.ScriptEnd, original.Length - parts.ScriptEnd); // m_PathName vb. kalan alanlar
        return ms.ToArray();
    }

    private static byte[] ReadAlignedBytes(byte[] data, bool bigEndian, ref int pos)
    {
        if (data.Length - pos < 4) throw new InvalidDataException("TextAsset verisi kısa.");
        int len = bigEndian ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos)) : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        if (len < 0 || len > data.Length - pos) throw new InvalidDataException("TextAsset metin uzunluğu geçersiz.");
        var bytes = data.AsSpan(pos, len).ToArray();
        pos += len;
        pos += (4 - pos % 4) % 4;
        if (pos > data.Length) throw new InvalidDataException("TextAsset hizalaması geçersiz.");
        return bytes;
    }

    // ─── Yardımcılar ─────────────────────────────────────────────────────────

    private static bool IsText(byte[] bytes, out string text)
    {
        text = string.Empty;
        if (bytes.Length == 0 || bytes.AsSpan(0, Math.Min(bytes.Length, 4096)).Contains((byte)0)) return false;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return false; }
        return text.Any(char.IsLetter);
    }

    private static string SniffExtension(string text)
    {
        var t = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (t.StartsWith('{') || t.StartsWith('[')) return "json";
        if (t.StartsWith('<')) return "xml";
        if (t.Contains("msgid ", StringComparison.Ordinal) && t.Contains("msgstr", StringComparison.Ordinal)) return "po";
        var firstLine = t.Split('\n', 2)[0];
        if (firstLine.Contains(',') || firstLine.Contains('\t')) return "csv";
        return "txt";
    }

    private static string MakePath(SerializedFileHandle handle, string kind, string name, long pathId, string ext)
    {
        var safe = Regex.Replace(string.IsNullOrWhiteSpace(name) ? "unnamed" : name, @"[\\/:*?""<>|#\x00-\x1F]", "_");
        return $"{handle.Label}/{kind}/{safe}#{pathId}.{ext}";
    }

    private static readonly string[] ExcludedNameParts =
    [
        "guid", "path", "url", "uri", "address", "hash", "shader", "assembly", "namespace", "classname", "typename",
        "scene", "prefab", "sprite", "texture", "material", "anim", "sound", "audio", "clip", "font", "icon", "tag",
        "layer", "eventname", "method", "callback", "collectionname", "identifier", "locale", "culture", "format"
    ];

    private static readonly Regex GuidLike = new(@"^[0-9a-fA-F]{32}$|^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$", RegexOptions.Compiled);
    private static readonly Regex LocaleCode = new(@"^[a-z]{2,3}([-_][A-Za-z]{2,4})*$", RegexOptions.Compiled);

    /// <summary>
    /// Bir MonoBehaviour metin alanının çevrilebilir olup olmadığına karar verir. Anahtar, kimlik, yol, GUID,
    /// dil kodu gibi oyunun çalışması için değişmemesi gereken değerler dışarıda bırakılır.
    /// </summary>
    public static bool IsTranslatableField(string path, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Any(char.IsLetter)) return false;

        var last = path[(path.LastIndexOf('.') + 1)..];
        int bracket = last.IndexOf('[');
        if (bracket >= 0) last = last[..bracket];
        var lower = last.ToLowerInvariant();
        var bare = lower.StartsWith("m_") ? lower[2..] : lower;

        if (bare is "name" or "editorclassidentifier" or "code" or "key" or "id" or "guid" or "class" or "ns" or "asm") return false;
        if (last.EndsWith("Id", StringComparison.Ordinal) || last.EndsWith("ID", StringComparison.Ordinal) ||
            last.EndsWith("Key", StringComparison.Ordinal) || last.EndsWith("Code", StringComparison.Ordinal))
            return false;
        if (ExcludedNameParts.Any(p => bare.Contains(p, StringComparison.Ordinal))) return false;

        var v = value.Trim();
        if (GuidLike.IsMatch(v) || LocaleCode.IsMatch(v) && v.Length <= 10 && v.Contains('-')) return false;
        bool hasSpace = v.Any(char.IsWhiteSpace);
        if (!hasSpace && (v.Contains('_') || v.Contains('/') || v.Contains('\\') || Regex.IsMatch(v, @"\.[A-Za-z0-9]{1,5}$")))
            return false; // tanımlayıcı veya dosya adı (UI_Start, Assets/x.png, file.json)

        return true;
    }

    public void Dispose()
    {
        _bundle?.Dispose();
        _fileStream?.Dispose();
    }
}
