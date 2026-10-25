using System.Buffers.Binary;
using System.Text;

namespace LocalizePak.IO.Formats.IoStore;

/// <summary>Bir pakette bulunan çevrilebilir metin (FText veya StringTable girdisi).</summary>
public readonly record struct GatheredText(string Namespace, string Key, string Source, bool FromStringTable);

/// <summary>
/// Pişirilmiş (cooked) Unreal paketlerinin (IoStore/zen veya klasik .uasset/.uexp) ham baytlarında çevrilebilir metinleri bulur.
/// Sürümsüz (unversioned) özellik serileştirmesinde tip bilgisi olmadığından (.usmap eşlemesi gerekmeden) FText'in
/// kendi serileştirme kalıbı aranır:
/// <list type="bullet">
/// <item>FText (Base geçmişi): <c>int32 Flags</c> · <c>int8 HistoryType = 0</c> · <c>FString Namespace</c> · <c>FString Key</c> · <c>FString SourceString</c></item>
/// <item>UStringTable: <c>FString TableNamespace</c> · <c>int32 Count</c> · <c>Count × (FString Key, FString SourceString)</c></item>
/// </list>
/// </summary>
public static class ZenTextScanner
{
    private const int MaxStringChars = 16 * 1024;
    private const int MaxStringTableEntries = 200_000;

    /// <summary>ETextFlag bitleri (Transient, CultureInvariant, ConvertedProperty, Immutable, InitializedFromString).</summary>
    private const int KnownTextFlags = 0x1F;

    private const int CultureInvariantFlag = 0x2;

    // Blueprint bayt kodu (Kismet): EX_TextConst + EBlueprintTextLiteralType::LocalizedText + 3 dize ifadesi
    private const byte ExTextConst = 0x29;
    private const byte LocalizedTextLiteral = 1;
    private const byte ExStringConst = 0x1F;
    private const byte ExUnicodeStringConst = 0x34;

    public static List<GatheredText> Scan(ReadOnlySpan<byte> data)
    {
        var results = new List<GatheredText>();
        for (int i = 0; i + 4 <= data.Length; i++)
        {
            if (data[i] == ExTextConst && data[i + 1] == LocalizedTextLiteral && TryReadScriptText(data, i, out var literal, out int literalEnd))
            {
                results.Add(literal);
                i = literalEnd - 1;
                continue;
            }

            if (TryReadStringTable(data, i, results, out int stEnd))
            {
                i = stEnd - 1;
                continue;
            }

            if (i >= 5 && data[i - 1] == 0 && TryReadBaseText(data, i, out var text, out int end))
            {
                results.Add(text);
                i = end - 1;
            }
        }
        return results;
    }

    private static bool TryReadBaseText(ReadOnlySpan<byte> data, int pos, out GatheredText text, out int end)
    {
        text = default;
        end = pos;
        int flags = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos - 5, 4));
        if ((flags & ~KnownTextFlags) != 0 || (flags & CultureInvariantFlag) != 0) return false;

        int p = pos;
        if (!TryReadFString(data, ref p, out var ns)) return false;
        if (!TryReadFString(data, ref p, out var key) || key.Length == 0 || !IsIdentifierLike(key)) return false;
        if (!TryReadFString(data, ref p, out var source) || string.IsNullOrWhiteSpace(source)) return false;
        if (!IsIdentifierLike(ns, allowEmpty: true)) return false;

        text = new GatheredText(ns, key, source, false);
        end = p;
        return true;
    }

    /// <summary>
    /// Blueprint fonksiyonlarındaki metin sabiti: <c>EX_TextConst</c> · <c>LocalizedText</c> · kaynak · anahtar · ad alanı
    /// (her biri <c>EX_StringConst</c> + \0 sonlu Latin-1 veya <c>EX_UnicodeStringConst</c> + \0\0 sonlu UTF-16).
    /// </summary>
    private static bool TryReadScriptText(ReadOnlySpan<byte> data, int pos, out GatheredText text, out int end)
    {
        text = default;
        end = pos;
        int p = pos + 2;
        if (!TryReadScriptString(data, ref p, out var source) || string.IsNullOrWhiteSpace(source)) return false;
        if (!TryReadScriptString(data, ref p, out var key) || key.Length == 0 || !IsIdentifierLike(key)) return false;
        if (!TryReadScriptString(data, ref p, out var ns) || !IsIdentifierLike(ns, allowEmpty: true)) return false;
        text = new GatheredText(ns, key, source, false);
        end = p;
        return true;
    }

    private static bool TryReadScriptString(ReadOnlySpan<byte> data, ref int pos, out string value)
    {
        value = string.Empty;
        if (pos >= data.Length) return false;
        byte token = data[pos];
        if (token == ExStringConst)
        {
            var rest = data[(pos + 1)..];
            int zero = rest[..Math.Min(rest.Length, MaxStringChars + 1)].IndexOf((byte)0);
            if (zero < 0) return false;
            var bytes = rest[..zero];
            foreach (var b in bytes)
                if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D)) return false;
            value = Encoding.Latin1.GetString(bytes);
            pos += 1 + zero + 1;
            return true;
        }
        if (token == ExUnicodeStringConst)
        {
            int start = pos + 1;
            int limit = Math.Min(data.Length - 1, start + MaxStringChars * 2);
            for (int k = start; k < limit; k += 2)
            {
                if (data[k] != 0 || data[k + 1] != 0) continue;
                var s = Encoding.Unicode.GetString(data[start..k]);
                foreach (char c in s)
                    if (c < 0x20 && c is not ('\t' or '\n' or '\r')) return false;
                value = s;
                pos = k + 2;
                return true;
            }
        }
        return false;
    }

    private static bool TryReadStringTable(ReadOnlySpan<byte> data, int pos, List<GatheredText> results, out int end)
    {
        end = pos;
        int p = pos;
        if (!TryReadFString(data, ref p, out var ns) || ns.Length == 0 || !IsIdentifierLike(ns)) return false;
        if (p + 4 > data.Length) return false;
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(p, 4));
        if (count < 1 || count > MaxStringTableEntries) return false;
        p += 4;

        var entries = new List<GatheredText>(Math.Min(count, 1024));
        for (int n = 0; n < count; n++)
        {
            if (!TryReadFString(data, ref p, out var key) || key.Length == 0 || !IsIdentifierLike(key)) return false;
            if (!TryReadFString(data, ref p, out var source)) return false;
            entries.Add(new GatheredText(ns, key, source, true));
        }

        // Tek girdili ve anahtarı da kaynağı da kısa bir "tablo" rastgele eşleşme olabilir; FText kalıbıyla çakışmasın
        if (count == 1 && entries[0].Source.Length < 2) return false;

        foreach (var e in entries)
            if (!string.IsNullOrWhiteSpace(e.Source)) results.Add(e);
        end = p;
        return true;
    }

    /// <summary>
    /// FString okur: pozitif uzunluk = Latin-1 bayt sayısı (sonda \0), negatif = UTF-16 karakter sayısı (sonda \0\0), 0 = boş.
    /// </summary>
    internal static bool TryReadFString(ReadOnlySpan<byte> data, ref int pos, out string value)
    {
        value = string.Empty;
        if (pos + 4 > data.Length) return false;
        int length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
        if (length == 0)
        {
            pos += 4;
            return true;
        }

        if (length > 0)
        {
            if (length > MaxStringChars || pos + 4 + length > data.Length) return false;
            var bytes = data.Slice(pos + 4, length);
            if (bytes[^1] != 0) return false;
            bytes = bytes[..^1];
            foreach (var b in bytes)
                if (b == 0 || (b < 0x20 && b is not (0x09 or 0x0A or 0x0D))) return false;
            value = Encoding.Latin1.GetString(bytes);
            pos += 4 + length;
            return true;
        }

        if (length == int.MinValue) return false;
        int chars = -length;
        if (chars > MaxStringChars || pos + 4 + chars * 2L > data.Length) return false;
        var utf16 = data.Slice(pos + 4, chars * 2);
        if (utf16[^1] != 0 || utf16[^2] != 0) return false;
        var s = Encoding.Unicode.GetString(utf16[..^2]);
        for (int k = 0; k < s.Length; k++)
        {
            char c = s[k];
            if (c == 0 || (c < 0x20 && c is not ('\t' or '\n' or '\r'))) return false;
            if (char.IsHighSurrogate(c) && (k + 1 >= s.Length || !char.IsLowSurrogate(s[++k]))) return false;
            if (char.IsLowSurrogate(c)) return false;
        }
        value = s;
        pos += 4 + chars * 2;
        return true;
    }

    /// <summary>Ad alanı/anahtar: yazdırılabilir, satır sonu içermeyen kısa metin.</summary>
    private static bool IsIdentifierLike(string s, bool allowEmpty = false)
    {
        if (s.Length == 0) return allowEmpty;
        if (s.Length > 512) return false;
        foreach (char c in s)
            if (char.IsControl(c)) return false;
        return true;
    }
}
