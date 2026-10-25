using System.Text;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Formats;

/// <summary>
/// Çok sütunlu yerelleştirme tablolarını (CSV / TSV / noktalı virgül) biçimini koruyarak okur ve yazar.
/// <list type="bullet">
/// <item>RFC 4180 tırnaklama: <c>"a, b"</c>, <c>""</c> kaçışı ve hücre içi satır sonları.</item>
/// <item>Sütun rolleri başlıktan çıkarılır: anahtar (ilk sütun), kaynak dil (ör. English) ve hedef dil (ör. Türkçe / Turkish).</item>
/// <item>Hedef dil sütunu yoksa çeviri kaynak sütunun üzerine yazılır (oyun yalnızca o sütunu okuyorsa doğru davranış).</item>
/// <item>Geri yazarken yalnızca hedef sütun değişir; diğer diller, başlık, tırnaklama, satır sonu ve BOM aynen korunur.</item>
/// </list>
/// </summary>
public static class DelimitedTableCodec
{
    private static readonly char[] CandidateDelimiters = ['\t', ',', ';'];

    /// <summary>ISO kodu → başlıklarda görülen dil adları (küçük harf, parantez öncesi).</summary>
    private static readonly Dictionary<string, string[]> LanguageAliases = new()
    {
        ["en"] = ["en", "en-us", "en_us", "en-gb", "eng", "english", "ingilizce", "i̇ngilizce"],
        ["tr"] = ["tr", "tr-tr", "tr_tr", "tur", "turkish", "türkçe", "turkce", "türkce"],
        ["de"] = ["de", "de-de", "ger", "deu", "german", "deutsch", "almanca"],
        ["fr"] = ["fr", "fr-fr", "fra", "fre", "french", "français", "francais", "fransızca"],
        ["es"] = ["es", "es-es", "spa", "spanish", "español", "espanol", "ispanyolca"],
        ["it"] = ["it", "it-it", "ita", "italian", "italiano", "italyanca"],
        ["pt"] = ["pt", "pt-br", "pt-pt", "por", "portuguese", "português", "portugues", "portekizce"],
        ["ru"] = ["ru", "ru-ru", "rus", "russian", "русский", "rusça"],
        ["pl"] = ["pl", "pl-pl", "pol", "polish", "polski", "lehçe"],
        ["uk"] = ["uk", "uk-ua", "ukr", "ukrainian", "українська"],
        ["ja"] = ["ja", "ja-jp", "jpn", "japanese", "日本語", "japonca"],
        ["ko"] = ["ko", "ko-kr", "kor", "korean", "한국어", "korece"],
        ["zh"] = ["zh", "zh-cn", "zh-tw", "zh-hans", "zh-hant", "chi", "chinese", "simplified chinese", "traditional chinese", "简体中文", "繁體中文", "中文", "çince"],
        ["nl"] = ["nl", "nl-nl", "dutch", "nederlands", "flemenkçe"],
        ["ar"] = ["ar", "arabic", "العربية", "arapça"],
    };

    private static readonly string[] KeyHeaderNames = ["key", "keys", "id", "ids", "stringid", "string id", "term", "terms", "name", "code", "tag"];

    /// <summary>
    /// İçerik tutarlı bir çok sütunlu tabloysa ayrıştırır.
    /// </summary>
    /// <param name="sourceLanguage">Kaynak dil ISO kodu ("auto" → İngilizce varsayılır).</param>
    /// <param name="targetLanguage">Hedef dil ISO kodu (ör. "tr").</param>
    public static bool TryParse(string content, bool hasBom, string sourceLanguage, string targetLanguage, out LocresFile file)
    {
        file = null!;
        if (string.IsNullOrWhiteSpace(content)) return false;
        content = content.TrimStart('﻿');
        if (LooksLikeKeyValueFile(content)) return false; // INI / .properties: "anahtar=metin, devamı" virgülden bölünmemeli

        List<List<DelimitedCell>>? best = null;
        List<string> bestEndings = [];
        char bestDelimiter = ',';
        int bestColumns = 0;
        foreach (var d in CandidateDelimiters)
        {
            if (!content.Contains(d)) continue;
            List<List<DelimitedCell>> rows;
            List<string> endings;
            try { rows = Tokenize(content, d, out endings); }
            catch (FormatException) { continue; }

            int columns = ConsistentColumnCount(rows);
            if (columns > bestColumns)
            {
                best = rows;
                bestEndings = endings;
                bestDelimiter = d;
                bestColumns = columns;
            }
        }
        if (best == null || bestColumns < 2) return false;

        var rowsFinal = best;
        int headerRow = FindHeaderRow(rowsFinal, out var languageByColumn);
        string src = string.IsNullOrEmpty(sourceLanguage) || sourceLanguage == "auto" ? "en" : sourceLanguage;

        int keyColumn = 0;
        if (languageByColumn.ContainsKey(0)) keyColumn = -1; // ilk sütun bir dil sütunuysa ayrı anahtar sütunu yok

        int FindColumn(string iso)
        {
            foreach (var (column, lang) in languageByColumn)
                if (lang == iso) return column;
            return -1;
        }

        // Kaynak: seçili kaynak dil → İngilizce → en çok metin içeren sütun
        int sourceColumn = FindColumn(src);
        if (sourceColumn < 0) sourceColumn = FindColumn("en");
        if (sourceColumn < 0) sourceColumn = GuessTextColumn(rowsFinal, headerRow, keyColumn);

        // Hedef: seçili hedef dilin sütunu; yoksa kaynak sütunun üzerine yazılır
        int targetColumn = FindColumn(targetLanguage);
        if (targetColumn < 0) targetColumn = sourceColumn;

        var table = new DelimitedTable
        {
            Delimiter = bestDelimiter,
            HasBom = hasBom,
            UsesQuotes = rowsFinal.Any(row => row.Any(cell => cell.Quoted)),
            Rows = rowsFinal,
            RowEndings = bestEndings,
            HeaderRow = headerRow,
            KeyColumn = keyColumn,
            SourceColumn = sourceColumn,
            TargetColumn = targetColumn
        };

        var nsName = headerRow >= 0 && targetColumn < rowsFinal[headerRow].Count ? rowsFinal[headerRow][targetColumn].Value : "Table";
        var ns = new LocresNamespace { Name = string.IsNullOrWhiteSpace(nsName) ? "Table" : nsName };
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        for (int r = 0; r < rowsFinal.Count; r++)
        {
            if (r == headerRow) continue;
            var row = rowsFinal[r];
            if (sourceColumn >= row.Count) continue;
            var sourceText = row[sourceColumn].Value;
            if (string.IsNullOrWhiteSpace(sourceText)) continue;

            var key = keyColumn >= 0 && keyColumn < row.Count && !string.IsNullOrWhiteSpace(row[keyColumn].Value)
                ? row[keyColumn].Value.Trim()
                : $"row_{r + 1}";
            if (!usedKeys.Add(key)) key = $"{key}#{r + 1}";

            var existing = targetColumn != sourceColumn && targetColumn < row.Count ? row[targetColumn].Value : string.Empty;
            bool alreadyTranslated = !string.IsNullOrWhiteSpace(existing) && existing != sourceText;

            var entry = new LocalizationEntry
            {
                Key = key,
                NamespaceName = ns.Name,
                SourceText = sourceText,
                TranslatedText = alreadyTranslated ? existing : sourceText,
                Status = alreadyTranslated ? TranslationStatus.Done : TranslationStatus.Pending
            };
            ns.Entries.Add(entry);
            table.EntryRows.Add((r, entry));
        }

        if (ns.Entries.Count == 0) return false;

        file = new LocresFile { Version = LocresVersion.V3, Table = table };
        file.Namespaces.Add(ns);
        return true;
    }

    /// <summary>Tabloyu, girdilerdeki çevirileri hedef sütuna yazarak özgün biçimiyle serileştirir.</summary>
    public static byte[] Serialize(DelimitedTable table)
    {
        foreach (var (rowIndex, entry) in table.EntryRows)
        {
            // Hiç çevrilmemiş satır (çeviri = kaynak) hedef hücresine dokunmaz; boş hedef İngilizce ile doldurulmaz
            if (entry.Status == TranslationStatus.Pending && entry.TranslatedText == entry.SourceText) continue;

            var row = table.Rows[rowIndex];
            while (row.Count <= table.TargetColumn) row.Add(new DelimitedCell());
            var translated = string.IsNullOrEmpty(entry.TranslatedText) ? row[table.TargetColumn].Value : entry.TranslatedText;
            if (string.IsNullOrEmpty(translated)) translated = entry.SourceText;
            row[table.TargetColumn].Value = translated;
        }

        var sb = new StringBuilder();
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0) sb.Append(table.Delimiter);
                AppendCell(sb, row[c], table);
            }
            sb.Append(r < table.RowEndings.Count ? table.RowEndings[r] : "\n");
        }

        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return table.HasBom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    private static void AppendCell(StringBuilder sb, DelimitedCell cell, DelimitedTable table)
    {
        var v = cell.Value;
        if (cell.RawText != null && v == cell.OriginalValue)
        {
            // Değişmeyen hücre dosyadaki haliyle yazılır (standart dışı tırnaklama dahil)
            sb.Append(cell.RawText);
            return;
        }
        bool mustQuote = cell.Quoted;
        if (!mustQuote)
        {
            // Değişen hücre: tabloyu bozacak karakterler varsa tırnakla. Tırnak kullanmayan dosyalarda (düz bölme)
            // metin içi " olduğu gibi kalır; aksi halde oyun "" kaçışını harfiyen gösterirdi.
            mustQuote = v.Contains(table.Delimiter) || v.Contains('\n') || v.Contains('\r') || v.StartsWith('"')
                || (table.UsesQuotes && v.Contains('"'));
        }
        if (!mustQuote)
        {
            sb.Append(v);
            return;
        }
        sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
    }

    /// <summary>
    /// RFC 4180 ayrıştırıcı (hücre içi satır sonu ve "" kaçışı). Satırlar "\r\n" veya "\n" ile biter; tırnaksız hücredeki
    /// tek başına "\r" metnin parçasıdır (dosyada hiç "\n" yoksa satır sonu sayılır). Son satırdaki boş satır üretilmez.
    /// </summary>
    internal static List<List<DelimitedCell>> Tokenize(string content, char delimiter, out List<string> rowEndings)
    {
        var rows = new List<List<DelimitedCell>>();
        var endings = new List<string>();
        var row = new List<DelimitedCell>();
        var field = new StringBuilder();
        bool quoted = false, inQuotes = false, fieldStarted = false;
        bool loneCrIsNewLine = !content.Contains('\n');
        int i = 0, fieldStart = 0;

        void EndField()
        {
            var value = field.ToString();
            row.Add(new DelimitedCell { Value = value, Quoted = quoted, OriginalValue = value, RawText = content[fieldStart..i] });
            field.Clear();
            quoted = false;
            fieldStarted = false;
        }

        while (i < content.Length)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false;
                    i++;
                    // kapanış tırnağından sonra yalnızca ayırıcı veya satır sonu gelebilir; değilse tırnaksız devam (gevşek)
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            if (c == '"' && !fieldStarted)
            {
                inQuotes = true;
                quoted = true;
                fieldStarted = true;
                i++;
                continue;
            }
            if (c == delimiter)
            {
                EndField();
                i++;
                fieldStart = i;
                continue;
            }
            bool crlf = c == '\r' && i + 1 < content.Length && content[i + 1] == '\n';
            if (c == '\n' || crlf || (c == '\r' && loneCrIsNewLine))
            {
                EndField();
                rows.Add(row);
                row = [];
                var ending = crlf ? "\r\n" : c.ToString();
                endings.Add(ending);
                i += ending.Length;
                fieldStart = i;
                continue;
            }
            field.Append(c);
            fieldStarted = true;
            i++;
        }

        if (inQuotes) throw new FormatException("Kapanmamış tırnak.");
        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            EndField();
            rows.Add(row);
            endings.Add(string.Empty);
        }
        rowEndings = endings;
        return rows;
    }

    /// <summary>Satırların çoğu ayırıcıdan önce '=' içeriyorsa (INI / .properties) tablo değildir.</summary>
    private static bool LooksLikeKeyValueFile(string content)
    {
        int lines = 0, keyValue = 0;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '[' or '#' or ';') continue;
            lines++;
            int eq = line.IndexOf('=');
            int delimiter = line.IndexOfAny(CandidateDelimiters);
            if (eq > 0 && (delimiter < 0 || eq < delimiter)) keyValue++;
        }
        return lines > 0 && keyValue * 2 >= lines;
    }

    /// <summary>Dil başlığı yoksa kaynak sütun: boşluk içeren metin hücresi en çok olan sütun (sayı/kimlik sütunları elenir).</summary>
    private static int GuessTextColumn(List<List<DelimitedCell>> rows, int headerRow, int keyColumn)
    {
        var scores = new Dictionary<int, int>();
        for (int r = 0; r < rows.Count; r++)
        {
            if (r == headerRow) continue;
            for (int c = 0; c < rows[r].Count; c++)
            {
                if (c == keyColumn) continue;
                var v = rows[r][c].Value;
                if (v.Contains(' ') && v.Any(char.IsLetter))
                    scores[c] = scores.GetValueOrDefault(c) + 1;
            }
        }
        if (scores.Count == 0) return keyColumn == 0 ? 1 : 0;
        return scores.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }

    /// <summary>
    /// Satırların çoğunun (≥ %80) paylaştığı sütun sayısı; tutarlı değilse 0.
    /// En az 2 dolu satır gerekir.
    /// </summary>
    private static int ConsistentColumnCount(List<List<DelimitedCell>> rows)
    {
        var nonEmpty = rows.Where(r => r.Count > 1 || (r.Count == 1 && r[0].Value.Length > 0)).ToList();
        if (nonEmpty.Count < 2) return 0;
        var mode = nonEmpty.GroupBy(r => r.Count).OrderByDescending(g => g.Count()).First();
        if (mode.Key < 2) return 0;
        double ratio = (double)mode.Count() / nonEmpty.Count;
        return ratio >= 0.8 ? mode.Key : 0;
    }

    /// <summary>İlk satırlar arasında dil adı veya anahtar başlığı içeren satırı başlık olarak bulur.</summary>
    private static int FindHeaderRow(List<List<DelimitedCell>> rows, out Dictionary<int, string> languageByColumn)
    {
        languageByColumn = [];
        for (int r = 0; r < Math.Min(3, rows.Count); r++)
        {
            var langs = new Dictionary<int, string>();
            for (int c = 0; c < rows[r].Count; c++)
            {
                if (TryMapLanguage(rows[r][c].Value, out var iso) && !langs.ContainsValue(iso))
                    langs[c] = iso;
            }
            bool keyHeader = rows[r].Count > 0 && KeyHeaderNames.Contains(Normalize(rows[r][0].Value));
            if (langs.Count >= 2 || (langs.Count >= 1 && keyHeader))
            {
                languageByColumn = langs;
                return r;
            }
            if (keyHeader) return r;
        }
        return -1;
    }

    private static string Normalize(string header)
    {
        var h = header.Trim().ToLowerInvariant();
        int paren = h.IndexOf('(');
        if (paren > 0) h = h[..paren].Trim();
        return h;
    }

    /// <summary>Başlık hücresini ISO dil koduna eşler ("English", "Türkçe", "pt-BR", "Español (España)" ...).</summary>
    public static bool TryMapLanguage(string header, out string iso)
    {
        var h = Normalize(header);
        foreach (var (code, aliases) in LanguageAliases)
        {
            if (aliases.Contains(h))
            {
                iso = code;
                return true;
            }
        }
        iso = string.Empty;
        return false;
    }
}
