using LocalizePak.Core.Enums;

namespace LocalizePak.Core.Models;

/// <summary>
/// Tek bir .pak arşiv girdisini (dosyasını) temsil eder.
/// </summary>
public sealed class PakEntry
{
    /// <summary>Arşiv içindeki sanal dosya yolu.</summary>
    public string VirtualPath { get; set; } = string.Empty;

    /// <summary>Veri bloğunun arşiv içindeki byte ofseti.</summary>
    public long DataOffset { get; set; }

    /// <summary>Sıkıştırılmamış veri boyutu.</summary>
    public long UncompressedSize { get; set; }

    /// <summary>Sıkıştırılmış veri boyutu (sıkıştırma yoksa UncompressedSize ile aynı).</summary>
    public long CompressedSize { get; set; }

    /// <summary>Kullanılan sıkıştırma yöntemi.</summary>
    public CompressionMethod Compression { get; set; } = CompressionMethod.None;

    /// <summary>Bu girdinin AES ile şifrelenip şifrelenmediği.</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>SHA1 veya SHA256 hash değeri (bütünlük doğrulama).</summary>
    public byte[]? Hash { get; set; }

    /// <summary>
    /// Larian Studios "Solid Archive" formatı bayrağı (Flags bit 6 = 0x40).
    /// True ise tüm dosyalar tek büyük bir solid LZ4 akışında paketlenmiştir;
    /// DataOffset solid stream içindeki RAW byte konumudur, ayrı compressed blob değildir.
    /// </summary>
    public bool IsSolid { get; set; }

    /// <summary>
    /// Solid archive formatında: solid stream'in disk üzerindeki toplam sıkıştırılmış boyutu.
    /// Solid stream başlangıç ofseti her zaman 0'dır, boyut dosya listesi ofsetine kadardır.
    /// </summary>
    public long SolidStreamSize { get; set; }

    /// <summary>
    /// Arşivde fiziksel olarak bulunmayan, paketlerdeki metinlerden üretilen girdi (ör. IoStore paketlerinden toplanan
    /// <c>Game.locres</c>). Yama olarak yazıldığında oyuna yeni dosya olarak eklenir.
    /// </summary>
    public bool IsGenerated { get; set; }

    /// <summary>Uzantıya göre bu girdinin lokalizasyon/metin dosyası olup olmadığı.</summary>
    public bool IsLocalizationFile =>
        VirtualPath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".locmeta", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".lsx", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".lang", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".strings", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".po", StringComparison.OrdinalIgnoreCase) ||
        VirtualPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bu girdinin tüm arşivin birleşik sanal girdisi olup olmadığı.</summary>
    public bool IsAllArchive => VirtualPath.StartsWith("🌐") || VirtualPath.Contains("[TÜM ARŞİV]");

    /// <summary>
    /// Bu dosyanın Unreal Engine motoruna (Engine.locres / Engine/...) ait olup olmadığı.
    /// </summary>
    public bool IsEngineFile =>
        !IsAllArchive &&
        (VirtualPath.Contains("Engine.locres", StringComparison.OrdinalIgnoreCase) ||
         VirtualPath.StartsWith("Engine/", StringComparison.OrdinalIgnoreCase) ||
         VirtualPath.StartsWith("../../../Engine/", StringComparison.OrdinalIgnoreCase) ||
         VirtualPath.Contains("/Engine/", StringComparison.OrdinalIgnoreCase) ||
         VirtualPath.Contains("\\Engine\\", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bu dosyanın oyunun kendi metinlerine (Game.locres vb.) ait olup olmadığı.
    /// </summary>
    public bool IsGameFile => !IsEngineFile && !IsAllArchive;

    /// <summary>
    /// Dosya yolundan (örn. .../Localization/Game/en/Game.locres) otomatik algılanan dil kodu.
    /// </summary>
    public string DetectedLanguageCode => DetectLanguageCode(VirtualPath);

    /// <summary>
    /// Algılanan dilin bayrak ve Türkçe adı (örn: "🇬🇧 İngilizce (en)").
    /// </summary>
    public string DetectedLanguageName => GetLanguageDisplayName(DetectedLanguageCode);

    /// <summary>
    /// UI listesinde gösterilecek zengin başlık (örn: "🇬🇧 İngilizce (en) ➔ Game.locres ⭐").
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            if (IsAllArchive) return "🌐 Tüm Dosyalar (Birleşik Görünüm)";
            if (string.IsNullOrWhiteSpace(VirtualPath)) return string.Empty;
            var fn = Path.GetFileName(VirtualPath);
            if (string.IsNullOrEmpty(fn)) return VirtualPath;

            var langName = DetectedLanguageName;

            if (IsGameFile)
            {
                if (DetectedLanguageCode.Equals("en", StringComparison.OrdinalIgnoreCase))
                    return $"{langName} ➔ {fn} ⭐ (Kaynak Çeviri Metinleri)";

                return string.IsNullOrEmpty(langName)
                    ? $"{fn} ⭐ (Oyun Metinleri)"
                    : $"{langName} ➔ {fn}";
            }

            if (IsEngineFile)
            {
                return string.IsNullOrEmpty(langName)
                    ? $"{fn} (⚠️ Unreal Motor Metinleri)"
                    : $"{langName} ➔ {fn} (⚠️ Motor Metinleri)";
            }

            return string.IsNullOrEmpty(langName) ? fn : $"{langName} ➔ {fn}";
        }
    }

    /// <summary>Yolun sadece dosya adı kısmı (UI gösterimi için açıklayıcı etiketlerle).</summary>
    public string FileName => DisplayTitle;

    /// <summary>Yolun dizin kısmı (UI gösterimi için).</summary>
    public string DirectoryPath
    {
        get
        {
            if (IsAllArchive) return "Arşivdeki tüm çeviri tabloları birleştirildi";
            if (string.IsNullOrWhiteSpace(VirtualPath)) return string.Empty;
            var dir = Path.GetDirectoryName(VirtualPath)?.Replace('\\', '/');
            return string.IsNullOrEmpty(dir) ? "/" : dir;
        }
    }

    /// <summary>Dosya türüne ve diline göre modern bayrak / WinUI emoji ikonu.</summary>
    public string FileIcon
    {
        get
        {
            if (IsAllArchive) return "🌐";
            var flag = GetLanguageFlag(DetectedLanguageCode);
            if (!string.IsNullOrEmpty(flag)) return flag;

            if (VirtualPath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase)) return "📜";
            if (VirtualPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "📋";
            if (VirtualPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || VirtualPath.EndsWith(".lsx", StringComparison.OrdinalIgnoreCase)) return "🏷️";
            if (VirtualPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || VirtualPath.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) return "📊";
            if (VirtualPath.EndsWith(".po", StringComparison.OrdinalIgnoreCase)) return "💬";
            return "📄";
        }
    }

    public static string DetectLanguageCode(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var normalized = path.Replace('\\', '/');

        // 1. Unreal Engine klasör yapısı: .../Localization/Game/{lang}/Game.locres
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            if (seg.Equals("Localization", StringComparison.OrdinalIgnoreCase) ||
                seg.Equals("Game", StringComparison.OrdinalIgnoreCase) ||
                seg.Equals("Engine", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < segments.Length - 1)
                {
                    var candidate = segments[i + 1];
                    if (IsKnownLanguageCode(candidate))
                        return candidate.ToLowerInvariant();
                }
            }
        }

        // 2. Doğrudan dosyanın bir üst dizini kontrolü (örn. .../en/Game.locres)
        var dirName = Path.GetFileName(Path.GetDirectoryName(path)?.Replace('\\', '/'));
        if (!string.IsNullOrEmpty(dirName) && IsKnownLanguageCode(dirName))
        {
            return dirName.ToLowerInvariant();
        }

        // 3. Dosya adının kendisi bir dil mi? (örn. English.xml, Turkish.json, fr.po)
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(path);
        if (IsKnownLanguageCode(fileNameWithoutExt))
        {
            return fileNameWithoutExt.ToLowerInvariant();
        }
        var mappedFromFullName = MapLanguageNameToCode(fileNameWithoutExt);
        if (!string.IsNullOrEmpty(mappedFromFullName))
        {
            return mappedFromFullName;
        }

        return string.Empty;
    }

    private static bool IsKnownLanguageCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var clean = code.Trim().ToLowerInvariant();
        return clean is "en" or "en-us" or "en-gb" or "de" or "de-de" or "fr" or "fr-fr"
            or "es" or "es-es" or "es-419" or "es-mx" or "it" or "it-it" or "ja" or "ja-jp"
            or "ko" or "ko-kr" or "ru" or "ru-ru" or "zh" or "zh-hans" or "zh-cn" or "zh-sg"
            or "zh-hant" or "zh-tw" or "zh-hk" or "pt" or "pt-pt" or "pt-br" or "pl" or "pl-pl"
            or "tr" or "tr-tr" or "ar" or "ar-sa" or "nl" or "nl-nl" or "sv" or "sv-se"
            or "no" or "nb" or "nn" or "da" or "da-dk" or "fi" or "fi-fi" or "cs" or "cs-cz"
            or "hu" or "hu-hu" or "uk" or "uk-ua" or "vi" or "vi-vn" or "th" or "th-th"
            or "id" or "id-id" or "el" or "el-gr";
    }

    private static string MapLanguageNameToCode(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var lower = name.ToLowerInvariant();
        if (lower.Contains("english") || lower.Contains("ingilizce")) return "en";
        if (lower.Contains("german") || lower.Contains("deutsch") || lower.Contains("almanca")) return "de";
        if (lower.Contains("french") || lower.Contains("francais") || lower.Contains("fransizca")) return "fr";
        if (lower.Contains("spanish") || lower.Contains("espanol") || lower.Contains("ispanyolca")) return "es";
        if (lower.Contains("italian") || lower.Contains("italiano") || lower.Contains("italyanca")) return "it";
        if (lower.Contains("japanese") || lower.Contains("japonca")) return "ja";
        if (lower.Contains("korean") || lower.Contains("korece")) return "ko";
        if (lower.Contains("russian") || lower.Contains("rusca")) return "ru";
        if (lower.Contains("chinese") || lower.Contains("cince")) return "zh-hans";
        if (lower.Contains("portuguese") || lower.Contains("portekizce")) return "pt";
        if (lower.Contains("polish") || lower.Contains("lehce")) return "pl";
        if (lower.Contains("turkish") || lower.Contains("turkce")) return "tr";
        return string.Empty;
    }

    public static string GetLanguageFlag(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var clean = code.Trim().ToLowerInvariant();
        return clean switch
        {
            "en" or "en-us" or "en-gb" => "🇬🇧",
            "de" or "de-de" => "🇩🇪",
            "fr" or "fr-fr" => "🇫🇷",
            "es" or "es-es" => "🇪🇸",
            "es-419" or "es-mx" => "🇲🇽",
            "it" or "it-it" => "🇮🇹",
            "ja" or "ja-jp" => "🇯🇵",
            "ko" or "ko-kr" => "🇰🇷",
            "ru" or "ru-ru" => "🇷🇺",
            "zh" or "zh-hans" or "zh-cn" or "zh-sg" => "🇨🇳",
            "zh-hant" or "zh-tw" or "zh-hk" => "🇹🇼",
            "pt" or "pt-pt" => "🇵🇹",
            "pt-br" => "🇧🇷",
            "pl" or "pl-pl" => "🇵🇱",
            "tr" or "tr-tr" => "🇹🇷",
            "ar" or "ar-sa" => "🇸🇦",
            "nl" or "nl-nl" => "🇳🇱",
            "sv" or "sv-se" => "🇸🇪",
            "no" or "nb" or "nn" => "🇳🇴",
            "da" or "da-dk" => "🇩🇰",
            "fi" or "fi-fi" => "🇫🇮",
            "cs" or "cs-cz" => "🇨🇿",
            "hu" or "hu-hu" => "🇭🇺",
            "uk" or "uk-ua" => "🇺🇦",
            "vi" or "vi-vn" => "🇻🇳",
            "th" or "th-th" => "🇹🇭",
            "id" or "id-id" => "🇮🇩",
            "el" or "el-gr" => "🇬🇷",
            _ => "🌐"
        };
    }

    public static string GetLanguageDisplayName(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var clean = code.Trim().ToLowerInvariant();
        return clean switch
        {
            "en" or "en-us" or "en-gb" => "🇬🇧 İngilizce (en)",
            "de" or "de-de" => "🇩🇪 Almanca (de)",
            "fr" or "fr-fr" => "🇫🇷 Fransızca (fr)",
            "es" or "es-es" => "🇪🇸 İspanyolca (es)",
            "es-419" or "es-mx" => "🇲🇽 Latin Amerika İspanyolcası (es-419)",
            "it" or "it-it" => "🇮🇹 İtalyanca (it)",
            "ja" or "ja-jp" => "🇯🇵 Japonca (ja)",
            "ko" or "ko-kr" => "🇰🇷 Korece (ko)",
            "ru" or "ru-ru" => "🇷🇺 Rusça (ru)",
            "zh" or "zh-hans" or "zh-cn" or "zh-sg" => "🇨🇳 Çince [Basitleştirilmiş] (zh-Hans)",
            "zh-hant" or "zh-tw" or "zh-hk" => "🇹🇼 Çince [Geleneksel] (zh-Hant)",
            "pt" or "pt-pt" => "🇵🇹 Portekizce (pt)",
            "pt-br" => "🇧🇷 Brezilya Portekizcesi (pt-BR)",
            "pl" or "pl-pl" => "🇵🇱 Lehçe (pl)",
            "tr" or "tr-tr" => "🇹🇷 Türkçe (tr)",
            "ar" or "ar-sa" => "🇸🇦 Arapça (ar)",
            "nl" or "nl-nl" => "🇳🇱 Felemenkçe (nl)",
            "sv" or "sv-se" => "🇸🇪 İsveççe (sv)",
            "no" or "nb" or "nn" => "🇳🇴 Norveççe (no)",
            "da" or "da-dk" => "🇩🇰 Danca (da)",
            "fi" or "fi-fi" => "🇫🇮 Fince (fi)",
            "cs" or "cs-cz" => "🇨🇿 Çekçe (cs)",
            "hu" or "hu-hu" => "🇭🇺 Macarca (hu)",
            "uk" or "uk-ua" => "🇺🇦 Ukraynaca (uk)",
            "vi" or "vi-vn" => "🇻🇳 Vietnamca (vi)",
            "th" or "th-th" => "🇹🇭 Tayca (th)",
            "id" or "id-id" => "🇮🇩 Endonezce (id)",
            "el" or "el-gr" => "🇬🇷 Yunanca (el)",
            _ => $"🌐 {code}"
        };
    }
}

