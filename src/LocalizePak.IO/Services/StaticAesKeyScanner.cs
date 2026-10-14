using System.Collections.Concurrent;
using LocalizePak.IO.Helpers;

namespace LocalizePak.IO.Services;

/// <summary>Statik taramayla bulunan AES anahtarı.</summary>
public sealed record StaticKeyResult(string KeyHex, string Method, long FileOffset);

/// <summary>
/// Oyunun çalıştırılabilir dosyasını (exe) ÇALIŞTIRMADAN tarayarak Unreal pak AES-256 anahtarını bulur.
/// Adaylar, pak'ın şifreli indeksinin ilk bloğu çözülerek doğrulanır; yanlış pozitif pratikte yoktur.
/// Yöntemler (hızlıdan yavaşa):
///  1. Yığına yazılan sabitler: 8 adet <c>mov dword ptr [reg+disp], imm32</c> komutu (UE derlemelerinde yaygın).
///  2. 4 adet 64-bit anlık sabit (<c>mov r64, imm64</c>) art arda.
///  3. Bitişik 32 bayt: tüm dosya ofsetlerinde kayan pencere (paralel).
/// RAM taramasının aksine anahtarın bellekte bitişik durması gerekmez, oyunun çalışması da gerekmez.
/// </summary>
public static class StaticAesKeyScanner
{
    public static StaticKeyResult? ScanFile(
        string exePath, string pakPath, Action<string>? log = null, CancellationToken ct = default)
    {
        var head = PakIndexKeyTester.ReadEncryptedIndexHead(pakPath)
            ?? throw new InvalidOperationException("Pak'ın indeksi şifreli değil ya da footer okunamadı; AES anahtarı gerekmiyor olabilir.");
        log?.Invoke($"{Path.GetFileName(exePath)} okunuyor...");
        var data = File.ReadAllBytes(exePath);
        return ScanBuffer(data, head, log, ct);
    }

    public static StaticKeyResult? ScanBuffer(
        byte[] data, byte[] encryptedIndexHead, Action<string>? log = null, CancellationToken ct = default)
    {
        using var tester = new PakIndexKeyTester(encryptedIndexHead);

        log?.Invoke("Yöntem 1: yığın sabitleri (imm32)...");
        var r = ScanStackImmediates32(data, tester, ct);
        if (r != null) return r;

        log?.Invoke("Yöntem 2: 64-bit sabitler (imm64)...");
        r = ScanImmediates64(data, tester, ct);
        if (r != null) return r;

        log?.Invoke("Yöntem 3: bitişik 32 bayt penceresi (paralel)...");
        return ScanContiguous(data, encryptedIndexHead, ct);
    }

    // ── Yöntem 1 ────────────────────────────────────────────────────────────
    private readonly record struct Imm32Hit(int Pos, int Disp, byte Reg, byte Sib, uint Value);

    private static StaticKeyResult? ScanStackImmediates32(byte[] d, PakIndexKeyTester tester, CancellationToken ct)
    {
        var group = new List<Imm32Hit>();
        StaticKeyResult? found = null;

        StaticKeyResult? Flush()
        {
            if (group.Count >= 8)
            {
                for (int i = 0; i + 8 <= group.Count; i++)
                {
                    var w = group.GetRange(i, 8);
                    foreach (var order in Orders(w))
                    {
                        var key = new byte[32];
                        for (int k = 0; k < 8; k++)
                            BitConverter.TryWriteBytes(key.AsSpan(k * 4), w[order[k]].Value);
                        if (tester.Test(key))
                            return new StaticKeyResult(ToHex(key), "stack-imm32", w[0].Pos);
                    }
                }
            }
            group.Clear();
            return null;
        }

        for (int i = 0; i + 7 < d.Length; i++)
        {
            if ((i & 0xFFFFF) == 0) ct.ThrowIfCancellationRequested();
            if (d[i] != 0xC7) continue;

            byte modrm = d[i + 1];
            int mod = modrm >> 6, reg = (modrm >> 3) & 7, rm = modrm & 7;
            if (reg != 0 || mod == 3 || (mod == 0 && rm == 5)) continue; // yalnızca mov r/m32, imm32 /0 bellek hedefleri
            int p = i + 2;
            byte sib = 0;
            if (rm == 4) { sib = d[p]; p++; }
            int disp = 0;
            if (mod == 1) { disp = (sbyte)d[p]; p += 1; }
            else if (mod == 2) { if (p + 4 > d.Length) continue; disp = BitConverter.ToInt32(d, p); p += 4; }
            else if (mod == 0 && rm != 4) { /* [reg] */ }
            else if (mod == 0 && rm == 4 && (sib & 7) == 5) { disp = BitConverter.ToInt32(d, p); p += 4; }
            if (p + 4 > d.Length) continue;

            uint imm = BitConverter.ToUInt32(d, p);
            var hit = new Imm32Hit(i, disp, modrm, sib, imm);

            if (group.Count > 0 && i - group[^1].Pos > 24)
            {
                found = Flush();
                if (found != null) return found;
            }
            group.Add(hit);
            i = p + 3;
        }

        return Flush();
    }

    private static IEnumerable<int[]> Orders(List<Imm32Hit> w)
    {
        var idx = Enumerable.Range(0, w.Count).ToArray();
        yield return idx;                                                  // komut sırası
        yield return idx.OrderBy(j => w[j].Disp).ToArray();                // artan yığın adresi
        yield return idx.OrderByDescending(j => w[j].Disp).ToArray();      // azalan yığın adresi
    }

    // ── Yöntem 2 ────────────────────────────────────────────────────────────
    private static StaticKeyResult? ScanImmediates64(byte[] d, PakIndexKeyTester tester, CancellationToken ct)
    {
        var hits = new List<(int Pos, ulong Value)>();
        var key = new byte[32];

        StaticKeyResult? Flush()
        {
            for (int i = 0; i + 4 <= hits.Count; i++)
            {
                for (int k = 0; k < 4; k++) BitConverter.TryWriteBytes(key.AsSpan(k * 8), hits[i + k].Value);
                if (tester.Test(key)) return new StaticKeyResult(ToHex(key), "stack-imm64", hits[i].Pos);
            }
            hits.Clear();
            return null;
        }

        for (int i = 0; i + 10 <= d.Length; i++)
        {
            if ((i & 0xFFFFF) == 0) ct.ThrowIfCancellationRequested();
            if ((d[i] != 0x48 && d[i] != 0x49) || d[i + 1] < 0xB8 || d[i + 1] > 0xBF) continue; // REX.W B8+r imm64
            if (hits.Count > 0 && i - hits[^1].Pos > 40)
            {
                var f = Flush();
                if (f != null) return f;
            }
            hits.Add((i, BitConverter.ToUInt64(d, i + 2)));
            i += 9;
        }

        return Flush();
    }

    // ── Yöntem 3 ────────────────────────────────────────────────────────────
    private static StaticKeyResult? ScanContiguous(byte[] d, byte[] head, CancellationToken ct)
    {
        int last = d.Length - 32;
        if (last < 0) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var result = new ConcurrentBag<StaticKeyResult>();
        int chunk = 1 << 20;
        int chunks = (last + chunk) / chunk;

        try
        {
            Parallel.For(0, chunks, new ParallelOptions { CancellationToken = cts.Token }, () => new PakIndexKeyTester(head),
                (c, _, t) =>
                {
                    int from = c * chunk, to = Math.Min(last + 1, from + chunk);
                    for (int i = from; i < to; i++)
                    {
                        int zeros = 0;
                        for (int j = 0; j < 32; j++) if (d[i + j] == 0) zeros++;
                        if (zeros > 6) continue;
                        if (t.Test(d.AsSpan(i, 32)))
                        {
                            result.Add(new StaticKeyResult(ToHex(d.AsSpan(i, 32)), "contiguous", i));
                            cts.Cancel();
                            break;
                        }
                    }
                    return t;
                },
                t => t.Dispose());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

        ct.ThrowIfCancellationRequested();
        return result.FirstOrDefault();
    }

    private static string ToHex(ReadOnlySpan<byte> key) => "0x" + Convert.ToHexString(key);
}
