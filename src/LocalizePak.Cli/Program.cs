using System.Text.Json;
using System.Text.RegularExpressions;
using LocalizePak.Core.Models;
using LocalizePak.IO;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Helpers;
using LocalizePak.IO.Services;

namespace LocalizePak.Cli;

/// <summary>GLS komut satırı aracı: arayüz olmadan pak açma, çıkarma, .locres düzenleme ve yama paketi üretme.</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const string Usage = """
        gls — Game Localization System komut satırı

        KULLANIM
          gls scan-key  --pak <pak> --exe <oyun.exe>            Exe'yi çalıştırmadan statik AES anahtar taraması
          gls scan-key  --pak <pak> --process <ad> [--timeout 120]   Çalışan oyunun RAM'inde tarama (yalnızca Windows)
          gls list      <pak> [--key <hex>] [--filter <regex>]  Pak içeriğini listele
          gls extract   <pak> --out <klasör> [--key <hex>] [--prefix a;b] [--ext .uasset,.locres]
                        [--filter <regex>] [--max-size <bayt>]  Dosyaları çıkar
          gls locres dump   <dosya.locres>                      Özet ve ilk satırlar
          gls locres export <dosya.locres> <çıktı.json>         Çeviri şablonu: [{namespace,key,source,text}]
          gls locres apply  <kaynak.locres> <çeviri.json> <çıktı.locres>   Çeviriyi uygula (hash'ler korunur)
          gls pack      <klasör> --out <yama.pak> [--mount ../../../] [--key <hex>]   Yama paketi (_P.pak) üret

        ORTAM DEĞİŞKENLERİ
          GLS_OODLE_PATH             Oodle kütüphanesinin yolu (Oodle ile sıkıştırılmış paklar için)
          GLS_ALLOW_OODLE_DOWNLOAD=1 Oodle'ı üçüncü taraf adresten indirmeye izin ver (varsayılan: kapalı)
        """;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            var rest = args[1..];
            return args[0] switch
            {
                "scan-key" => await ScanKey(rest),
                "list" => await List(rest),
                "extract" => await Extract(rest),
                "locres" => await Locres(rest),
                "pack" => await Pack(rest),
                _ => Fail($"Bilinmeyen komut: {args[0]}\n\n{Usage}")
            };
        }
        catch (OperationCanceledException)
        {
            return Fail("İptal edildi veya zaman sınırı doldu.");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    // ── scan-key ────────────────────────────────────────────────────────────
    private static async Task<int> ScanKey(string[] a)
    {
        var o = Options.Parse(a);
        var pak = o.Require("pak");

        if (o.Get("exe") is { } exe)
        {
            var r = StaticAesKeyScanner.ScanFile(exe, pak, Console.WriteLine);
            if (r == null) return Fail("Statik taramada anahtar bulunamadı. --process ile RAM taramasını deneyin.");
            Console.WriteLine($"ANAHTAR: {r.KeyHex}\nyöntem: {r.Method}, dosya ofseti: 0x{r.FileOffset:X}");
            return 0;
        }

        if (o.Get("process") is { } name)
        {
            if (!OperatingSystem.IsWindows()) return Fail("RAM taraması yalnızca Windows'ta çalışır.");
            var timeout = TimeSpan.FromSeconds(int.Parse(o.Get("timeout") ?? "300"));
            var procs = RamAesExtractor.GetRunningProcesses()
                .Where(p => p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (procs.Count == 0) return Fail($"'{name}' adlı çalışan süreç yok.");

            var progress = new Progress<(int Progress, string Status)>(p => Console.Write($"\rtarama: %{p.Progress}   "));
            foreach (var p in procs)
            {
                Console.WriteLine($"{p.ProcessName} ({p.Id}) taranıyor, sınır {timeout.TotalSeconds:0} sn...");
                var key = await RamAesExtractor.ScanProcessForAesKeyAsync(p.Id, pak, progress, default);
                Console.WriteLine();
                if (key != null) { Console.WriteLine($"ANAHTAR: {key}"); return 0; }
            }
            return Fail("RAM taramasında anahtar bulunamadı (anahtar bellekte bitişik tutulmuyor olabilir; --exe ile statik tarayın).");
        }

        return Fail("--exe <oyun.exe> veya --process <ad> belirtin.");
    }

    // ── list / extract ──────────────────────────────────────────────────────
    private static async Task<int> List(string[] a)
    {
        var o = Options.Parse(a);
        var archive = await new PakService().OpenAsync(o.Positional(0), o.Get("key"));
        var rx = o.Get("filter") is { } f ? new Regex(f, RegexOptions.IgnoreCase) : null;
        int n = 0;
        foreach (var e in archive.Entries.Where(e => rx == null || rx.IsMatch(e.VirtualPath)))
        {
            Console.WriteLine($"{e.UncompressedSize,12}  {e.VirtualPath}");
            n++;
        }
        Console.Error.WriteLine($"{n} / {archive.Entries.Count} girdi ({archive.Format}, sürüm {archive.Version}, mount '{archive.MountPoint}')");
        return 0;
    }

    private static async Task<int> Extract(string[] a)
    {
        var o = Options.Parse(a);
        var pakPath = o.Positional(0);
        var outDir = o.Require("out");
        var svc = new PakService();
        var archive = await svc.OpenAsync(pakPath, o.Get("key"));

        var prefixes = o.Get("prefix")?.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var exts = o.Get("ext")?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLowerInvariant()).ToHashSet();
        var rx = o.Get("filter") is { } f ? new Regex(f, RegexOptions.IgnoreCase) : null;
        long max = long.Parse(o.Get("max-size") ?? long.MaxValue.ToString());

        var sel = archive.Entries.Where(e =>
            (prefixes == null || prefixes.Any(p => e.VirtualPath.StartsWith(p, StringComparison.OrdinalIgnoreCase))) &&
            (exts == null || exts.Contains(Path.GetExtension(e.VirtualPath).ToLowerInvariant())) &&
            (rx == null || rx.IsMatch(e.VirtualPath)) && e.UncompressedSize <= max).ToList();

        Console.WriteLine($"{sel.Count} dosya çıkarılacak...");
        int ok = 0, fail = 0;
        foreach (var e in sel)
        {
            try
            {
                // Arşiv içi yol "../" veya mutlak yol içerebilir (zip-slip); çıktı klasörü dışına yazmayı reddet.
                var dest = SafeIo.ResolveChildPath(outDir, e.VirtualPath);
                await using var s = await svc.ExtractEntryAsync(archive, e);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await using var fs = File.Create(dest);
                await s.CopyToAsync(fs);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                Console.Error.WriteLine($"HATA {e.VirtualPath}: {ex.Message}");
                if (fail >= 5 && ok == 0) return Fail("Art arda hata; durduruldu. Oodle durumu: " + (UnrealCuePakHandler.OodleStatus ?? "bilinmiyor"));
            }
        }
        Console.WriteLine($"bitti: {ok} başarılı, {fail} hatalı");
        return fail == 0 ? 0 : 2;
    }

    // ── locres ──────────────────────────────────────────────────────────────
    private static async Task<int> Locres(string[] a)
    {
        if (a.Length == 0) return Fail("locres alt komutu gerekli: dump | export | apply");
        var sub = a[0];
        var o = Options.Parse(a[1..]);

        if (sub == "dump")
        {
            var f = LocresCodec.Read(await File.ReadAllBytesAsync(o.Positional(0)));
            Console.WriteLine($"sürüm={f.Version} namespace={f.Namespaces.Count} girdi={f.TotalEntryCount}");
            foreach (var e in f.AllEntries.Take(15))
                Console.WriteLine($"  [{e.NamespaceName}] {e.Key}: {Trunc(e.SourceText)}");
            return 0;
        }

        if (sub == "export")
        {
            var f = LocresCodec.Read(await File.ReadAllBytesAsync(o.Positional(0)));
            var rows = f.Namespaces.SelectMany(n => n.Entries.Select(e =>
                new Row(n.Name, e.Key, e.SourceText, string.Empty))).ToList();
            await File.WriteAllTextAsync(o.Positional(1), JsonSerializer.Serialize(rows, Json));
            Console.WriteLine($"{rows.Count} satır -> {o.Positional(1)} (text boş = çevrilmedi, orijinal korunur)");
            return 0;
        }

        if (sub == "apply")
        {
            var f = LocresCodec.Read(await File.ReadAllBytesAsync(o.Positional(0)));
            var rows = JsonSerializer.Deserialize<List<Row>>(await File.ReadAllTextAsync(o.Positional(1)), Json) ?? [];
            var map = rows.Where(r => !string.IsNullOrEmpty(r.text))
                          .ToDictionary(r => (r.@namespace, r.key), r => r.text);
            int applied = 0, warnings = 0;
            foreach (var e in f.AllEntries)
            {
                if (!map.TryGetValue((e.NamespaceName, e.Key), out var text)) continue;
                e.TranslatedText = text;
                applied++;
                if (!TokensMatch(e.SourceText, text))
                {
                    warnings++;
                    Console.Error.WriteLine($"UYARI [{e.NamespaceName}] {e.Key}: değişken/etiketler kaynakla uyuşmuyor");
                }
            }
            int total = f.TotalEntryCount;
            if (o.Get("only-translated") != null)
            {
                foreach (var ns in f.Namespaces)
                    ns.Entries.RemoveAll(e => !map.ContainsKey((e.NamespaceName, e.Key)));
                f.Namespaces.RemoveAll(ns => ns.Entries.Count == 0);
            }
            await File.WriteAllBytesAsync(o.Positional(2), LocresCodec.Write(f));
            Console.WriteLine($"{applied}/{total} satır uygulandı ({f.TotalEntryCount} satır yazıldı), {warnings} uyarı -> {o.Positional(2)}");
            return warnings == 0 ? 0 : 3;
        }

        return Fail($"Bilinmeyen locres alt komutu: {sub}");
    }

    // ── pack ────────────────────────────────────────────────────────────────
    private static async Task<int> Pack(string[] a)
    {
        var o = Options.Parse(a);
        var dir = Path.GetFullPath(o.Positional(0));
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal)
            .Select(p => (VirtualPath: Path.GetRelativePath(dir, p).Replace('\\', '/'), Data: File.ReadAllBytes(p))).ToList();
        if (files.Count == 0) return Fail("Klasörde dosya yok.");

        await new PakService().WriteAsync(o.Require("out"), o.Get("mount") ?? "../../../", files, default,
            LocalizePak.Core.Enums.PakFormat.Unreal, o.Get("key"));
        Console.WriteLine($"{files.Count} dosya -> {o.Require("out")}");
        return 0;
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────
    private sealed record Row(string @namespace, string key, string source, string text);

    private static readonly Regex TokenRx = new(@"\{[^{}]*\}|%[sdif]|<[^<>]+>|\[/?[a-zA-Z]+\]|\\n", RegexOptions.Compiled);

    private static bool TokensMatch(string source, string target)
    {
        var s = TokenRx.Matches(source).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal);
        var t = TokenRx.Matches(target).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal);
        return s.SequenceEqual(t);
    }

    private static string Trunc(string s) => s.Length <= 70 ? s.Replace("\n", "\\n") : s[..70].Replace("\n", "\\n") + "…";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private sealed class Options
    {
        private readonly List<string> _pos = [];
        private readonly Dictionary<string, string> _opts = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    var k = args[i][2..];
                    o._opts[k] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
                }
                else o._pos.Add(args[i]);
            }
            return o;
        }

        public string? Get(string k) => _opts.GetValueOrDefault(k);
        public string Require(string k) => Get(k) ?? throw new ArgumentException($"--{k} gerekli.");
        public string Positional(int i) => i < _pos.Count ? _pos[i] : throw new ArgumentException($"{i + 1}. argüman eksik.");
    }
}
