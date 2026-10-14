using System.Text;
using System.Text.RegularExpressions;

namespace LocalizePak.Translation.Services;

/// <summary>
/// Sıfır AI jetonu harcayarak oyun içi arayüz taşmalarını (text overflow),
/// kırpılmaları (truncation) ve hardcoded metinleri anında test etmeyi sağlayan
/// endüstri standardı Pseudolocalization (Sahte Yerelleştirme) motoru.
/// </summary>
public static class PseudolocalizationEngine
{
    private static readonly Dictionary<char, char> GlyphMap = new()
    {
        ['a'] = 'à', ['A'] = 'Å',
        ['b'] = 'ƀ', ['B'] = 'Ɓ',
        ['c'] = 'ç', ['C'] = 'Ç',
        ['d'] = 'đ', ['D'] = 'Ð',
        ['e'] = 'é', ['E'] = 'É',
        ['f'] = 'ƒ', ['F'] = 'Ƒ',
        ['g'] = 'ğ', ['G'] = 'Ğ',
        ['h'] = 'ħ', ['H'] = 'Ħ',
        ['i'] = 'î', ['I'] = 'Î',
        ['j'] = 'ĵ', ['J'] = 'Ĵ',
        ['k'] = 'ķ', ['K'] = 'Ķ',
        ['l'] = 'ł', ['L'] = 'Ł',
        ['m'] = 'm', ['M'] = 'M',
        ['n'] = 'ñ', ['N'] = 'Ñ',
        ['o'] = 'ö', ['O'] = 'Ö',
        ['p'] = 'p', ['P'] = 'P',
        ['q'] = 'q', ['Q'] = 'Q',
        ['r'] = 'ř', ['R'] = 'Ř',
        ['s'] = 'ş', ['S'] = 'Ş',
        ['t'] = 'ŧ', ['T'] = 'Ŧ',
        ['u'] = 'ü', ['U'] = 'Ü',
        ['v'] = 'v', ['V'] = 'V',
        ['w'] = 'ŵ', ['W'] = 'Ŵ',
        ['x'] = 'x', ['X'] = 'X',
        ['y'] = 'ŷ', ['Y'] = 'Ŷ',
        ['z'] = 'ž', ['Z'] = 'Ž',
    };

    // Değişkenleri koruma regex deseni: {0}, {PlayerName}, %s, %d, <color=...>, \n
    private static readonly Regex VariableRegex = new(
        @"(?:\{[0-9A-Za-z_]+\}|%[0-9]*[a-zA-Z]|<[^>]+>|\\n|\\r|\\t|&[a-zA-Z]+;)",
        RegexOptions.Compiled);

    /// <summary>
    /// Metni %40 uzatarak, aksanlı karakterlerle dönüştürür ve çevreleyici işaretler [~!! ... !!~] ekler.
    /// Değişkenler ve etiketler bozulmadan %100 korunur.
    /// </summary>
    public static string Transform(string? input, double expansionFactor = 0.40)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Sayı veya salt sembollerden oluşan satırlara pseudolocalization uygulama
        if (!input.Any(char.IsLetter)) return input;

        // 1. Değişken ve etiketleri tokenlaştırarak ayır
        var matches = VariableRegex.Matches(input);
        var sb = new StringBuilder();
        int lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                var textPart = input.Substring(lastIndex, match.Index - lastIndex);
                sb.Append(ConvertChars(textPart));
            }

            // Değişkeni / etiketi aynen koru
            sb.Append(match.Value);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < input.Length)
        {
            var textPart = input.Substring(lastIndex);
            sb.Append(ConvertChars(textPart));
        }

        var converted = sb.ToString();

        // 2. Metin boyuna göre %30-40 uzatma ekle (Arayüz taşma testi)
        int extraCharsNeeded = (int)Math.Ceiling(input.Length * expansionFactor);
        string padding = GeneratePadding(extraCharsNeeded);

        return $"[~!! {converted}{padding} !~]";
    }

    private static string ConvertChars(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(GlyphMap.TryGetValue(c, out var mapped) ? mapped : c);
        }
        return sb.ToString();
    }

    private static string GeneratePadding(int count)
    {
        if (count <= 0) return string.Empty;
        var pattern = " ~öüé~";
        var sb = new StringBuilder(" ");
        for (int i = 0; i < count; i++)
        {
            sb.Append(pattern[i % pattern.Length]);
        }
        return sb.ToString();
    }
}
