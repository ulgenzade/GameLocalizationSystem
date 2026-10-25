using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats.IoStore;

/// <summary>
/// Oyunun paketlerindeki (IoStore .ucas veya klasik .pak) FText ve StringTable metinlerini toplayıp
/// çevrilebilir bir <see cref="LocresFile"/> üretir. Oyun kendi <c>Game.locres</c> dosyasını hiç paketlememişse
/// (yerelleştirme hedefi yoksa) çeviriyi oyuna taşımanın yolu budur: üretilen .locres yama paketine
/// <c>&lt;Proje&gt;/Content/Localization/Game/&lt;kültür&gt;/Game.locres</c> olarak yazılır ve motor aynı
/// ad alanı/anahtar ile kaynak metin hash'i eşleşen her FText'i bu çeviriyle gösterir.
/// </summary>
public static class IoStoreTextGatherer
{
    /// <summary>Toplanan metinleri içeren .locres (sürüm 2: anahtar hash'i gerektirmez, tüm UE4.2x/UE5 sürümleri okur).</summary>
    public static LocresFile Gather(
        IEnumerable<(string Path, Func<byte[]> Read)> packages,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var list = packages
            .Where(p => IsGamePackage(p.Path))
            .ToList();

        // Okuma (açma/şifre çözme) ve tarama paralel; birleştirme paket sırasıyla yapılır ki sonuç kararlı olsun
        var scanned = new List<GatheredText>[list.Count];
        int done = 0;
        Parallel.For(0, list.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            try { scanned[i] = ZenTextScanner.Scan(list[i].Read()); }
            catch { scanned[i] = []; } // okunamayan (ör. şifreli/bozuk) paket atlanır
            progress?.Report((Interlocked.Increment(ref done), list.Count));
        });

        var byKey = new Dictionary<(string Ns, string Key), GatheredText>();
        var order = new List<(string Ns, string Key)>();
        foreach (var texts in scanned)
        {
            foreach (var text in texts)
            {
                var id = (text.Namespace, text.Key);
                if (byKey.TryAdd(id, text)) order.Add(id);
            }
        }

        var file = new LocresFile { Version = LocresVersion.V2 };
        foreach (var group in order.GroupBy(id => id.Ns))
        {
            var ns = new LocresNamespace { Name = group.Key };
            foreach (var id in group)
            {
                var text = byKey[id];
                ns.Entries.Add(new LocalizationEntry
                {
                    NamespaceName = text.Namespace,
                    Key = text.Key,
                    SourceText = text.Source,
                    TranslatedText = text.Source,
                    SourceStringHash = UnrealTextHash.StrCrc32(text.Source),
                    Status = TranslationStatus.Pending
                });
            }
            file.Namespaces.Add(ns);
        }
        return file;
    }

    /// <summary>Motorun kendi içerikleri (Engine/…) dışındaki paketler.</summary>
    public static bool IsGamePackage(string path)
    {
        var p = path.Replace('\\', '/').TrimStart('/');
        if (p.StartsWith("../../../", StringComparison.Ordinal)) p = p[9..];
        if (p.StartsWith("Engine/", StringComparison.OrdinalIgnoreCase)) return false;
        return p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase);
    }
}
