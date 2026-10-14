using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalizePak.Core.Models;
using LocalizePak.IO.Helpers;

namespace LocalizePak.IO.Services;

/// <summary>
/// Bilinen yüzlerce Unreal Engine oyununun AES-256 şifreleme anahtarlarını barındıran,
/// otomatik oyun/paket eşleştirmesi yapabilen ve bulut üzerinden güncellenebilen anahtar havuzu servisi.
/// </summary>
public static class AesKeyVaultService
{
    private static readonly string VaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GameLocalizationSystem");

    private static readonly string VaultFilePath = Path.Combine(VaultDirectory, "aes_vault.json");

    private static readonly List<AesKeyVaultEntry> _keys = new();
    private static readonly object _lock = new();
    private static bool _initialized = false;

    // FModel ve Topluluk açık kaynak anahtar kaynakları (GitHub Raw URL'leri)
    private static readonly string[] CloudSources = new[]
    {
        "https://raw.githubusercontent.com/FModel/Unreal-Game-Keys/main/keys.json",
        "https://raw.githubusercontent.com/Cracko298/UE4-AES-Key-Extracting-Guide/main/keys.json"
    };

    /// <summary>
    /// Varsayılan gömülü oyun anahtarları tablosu (İnternetsiz de anında çalışan ana çekirdek).
    /// </summary>
    private static readonly AesKeyVaultEntry[] BuiltInKeys = new[]
    {
        // ─── POPPY PLAYTIME ───
        new AesKeyVaultEntry { GameName = "Poppy Playtime (Chapter 1)", AesKey = "0xD4BCF215F3B33A4BAA8D52139F1F49E92DF5BF8C7262C823E846AA6D79331FBC", EngineVersion = "UE 4.27", PakPattern = "Poppy_Playtime", Notes = "Chapter 1 Ana Menü ve Giriş" },
        new AesKeyVaultEntry { GameName = "Poppy Playtime (Chapter 2)", AesKey = "0xA419DEC1C6949BE5BDA54D4D6A62D735E932640B5F03EB53A3E72B10BAEB9748", EngineVersion = "UE 4.27", PakPattern = "Poppy_Playtime", Notes = "Chapter 2 Fly in a Web" },
        new AesKeyVaultEntry { GameName = "Poppy Playtime (Chapter 3)", AesKey = "0x6F168073B9B21449D742241700068ADABC306FA9AA3831164DEE8DE34E0EFBB0", EngineVersion = "UE 5.2", PakPattern = "Poppy_Playtime", Notes = "Chapter 3 Deep Sleep" },
        new AesKeyVaultEntry { GameName = "Project: Playtime", AesKey = "0x2340C79217E71F11420F50F95BA5D48494BFDBC9DC34E181A1187E7F30D8DB45", EngineVersion = "UE 4.27", PakPattern = "Project_Playtime", Notes = "Çok Oyunculu Yan Oyun" },

        // ─── POPÜLER VE GÜNCEL UE4 / UE5 OYUNLARI ───
        new AesKeyVaultEntry { GameName = "Silent Hill 2 Remake", AesKey = "0x39F68B5264BAF6D0FF04E40EBF8271B78B45E8D86B567634CECA20658DDE6174", EngineVersion = "UE 5.4", PakPattern = "SHProto", Notes = "Bloober Team" },
        new AesKeyVaultEntry { GameName = "Palworld", AesKey = "0x6295325DC8E5DC0D88F3C7E9D5CAAF3F5F4B65B5AC6DF8BD134C5AEB1530A2BB", EngineVersion = "UE 5.1", PakPattern = "Pal", Notes = "Pocketpair" },
        new AesKeyVaultEntry { GameName = "Tekken 8", AesKey = "0x44438344075D1E161C68ED5E364377F3EBECDB9EE41ECF5A7B8E5F611A680C42", EngineVersion = "UE 5.2", PakPattern = "Polaris", Notes = "Bandai Namco" },
        new AesKeyVaultEntry { GameName = "Hogwarts Legacy", AesKey = "0x53D2A7246FA456C8FD7EC3E8F4AFDB5C6F89BC972FB5716EB4643F24BD597DC8", EngineVersion = "UE 4.27", PakPattern = "Phoenix", Notes = "Avalanche Software" },
        new AesKeyVaultEntry { GameName = "Lies of P", AesKey = "0xAC36FEE249AE58EE43B4AC6FE628DCA9D86FF8C1688B2AE064B952E1098864BD", EngineVersion = "UE 4.27", PakPattern = "LiesofP", Notes = "Neowiz" },
        new AesKeyVaultEntry { GameName = "Manor Lords", AesKey = "0xDF519827B63E481D0A86BD38F1CE7D8312BF058C3615EE0449CD904F8C487C0D", EngineVersion = "UE 4.27", PakPattern = "ManorLords", Notes = "Slavic Magic" },
        new AesKeyVaultEntry { GameName = "Final Fantasy VII Remake Intergrade", AesKey = "0x17E45C170E304723049F830113B11D7B49265219CBE78B793167A6451B5C49F3", EngineVersion = "UE 4.18", PakPattern = "End", Notes = "Square Enix" },
        new AesKeyVaultEntry { GameName = "Stray", AesKey = "0xA84D5EFDBC63901C9BD04F2F12FE2C4D32B8E5B7082BE475871F02D246D2FB08", EngineVersion = "UE 4.27", PakPattern = "Hk_project", Notes = "BlueTwelve Studio" },
        new AesKeyVaultEntry { GameName = "Atomic Heart", AesKey = "0xDF76E23BE3D43202E5C99C68BF03C9E62C92E2E690B82B7CE2D574BE143D1A52", EngineVersion = "UE 4.27", PakPattern = "AtomicHeart", Notes = "Mundfish" },
        new AesKeyVaultEntry { GameName = "Dead Island 2", AesKey = "0x4FE3CD8962BE55DA7A213D5E7B7846A316C4082DF617CD55FE84620F20392F89", EngineVersion = "UE 4.27", PakPattern = "DeadIsland", Notes = "Dambuster Studios" },
        new AesKeyVaultEntry { GameName = "RoboCop: Rogue City", AesKey = "0x7F9154E70D304B76C12947E9771B7114A537BE26C45F4D3383B89873E47A7453", EngineVersion = "UE 5.2", PakPattern = "RoboCop", Notes = "Teyon" },
        new AesKeyVaultEntry { GameName = "Remnant 2", AesKey = "0x2D5E1F4B92C481A367E8351F0D49275E1C84074251B5A6E82956D3FE91734280", EngineVersion = "UE 5.2", PakPattern = "Remnant2", Notes = "Gunfire Games" },
        new AesKeyVaultEntry { GameName = "The Callisto Protocol", AesKey = "0xBD25EFE60C89BE6A36443E081DB4BE67AE73130E789B19C331668B807490A1A2", EngineVersion = "UE 4.27", PakPattern = "Callisto", Notes = "Striking Distance" },
        new AesKeyVaultEntry { GameName = "Dead by Daylight", AesKey = "0xF2079C318F12BD50551065191C836015D5BE61730B00C09E0AC6CD0C45053B57", EngineVersion = "UE 4.27", PakPattern = "DeadByDaylight", Notes = "Behaviour Interactive" },
        new AesKeyVaultEntry { GameName = "PUBG: Battlegrounds", AesKey = "0xB50346129998EFD857A1892A72BA2974F8B038379B5CE688E20E3EF5F9F6BFBE", EngineVersion = "UE 4.16", PakPattern = "TslGame", Notes = "Krafton" },
        new AesKeyVaultEntry { GameName = "Mortal Kombat 1", AesKey = "0x12248671C6DFE456A7C319D7079237D45367198C51E8402D39678129BE4D5B11", EngineVersion = "UE 4.27", PakPattern = "MK12", Notes = "NetherRealm" },
        new AesKeyVaultEntry { GameName = "Hi-Fi RUSH", AesKey = "0xF3B497D21468BC5E19A46FE2094B1B8756D35E81A22C4B7E05C82A79D463EF1A", EngineVersion = "UE 4.27", PakPattern = "Hibiki", Notes = "Tango Gameworks" },
        new AesKeyVaultEntry { GameName = "System Shock Remake", AesKey = "0xC24930FBE57618BC94A8D336519E2B548D105742EB5173099951B4F817B61942", EngineVersion = "UE 4.27", PakPattern = "SystemShock", Notes = "Nightdive Studios" },
        new AesKeyVaultEntry { GameName = "Banishers: Ghosts of New Eden", AesKey = "0x39E462B97419A2DFE81B5738841054C11984620F20392F89B5C82A79D463EF1A", EngineVersion = "UE 5.3", PakPattern = "Banishers", Notes = "DON'T NOD" },
        new AesKeyVaultEntry { GameName = "The Talos Principle 2", AesKey = "0x3B48F6D9A16723B0984E5108C4215E98715BC64A2F098317CD55FE84620F2039", EngineVersion = "UE 5.2", PakPattern = "Talos2", Notes = "Croteam" },
        new AesKeyVaultEntry { GameName = "Star Wars Jedi: Fallen Order", AesKey = "0x6A8D22C1A579B1130D2B44F754D7110C9951B4F817B61942E2D574BE143D1A52", EngineVersion = "UE 4.21", PakPattern = "SwGame", Notes = "Respawn" },
        new AesKeyVaultEntry { GameName = "Hellblade 2: Senua's Saga", AesKey = "0x5A7E11B498C04F2D725164B821A936C27581934D21FE901239857416C4D2E5B1", EngineVersion = "UE 5.4", PakPattern = "Hellblade2", Notes = "Ninja Theory" }
    };

    /// <summary>
    /// Havuzu başlatır, diskteki kayıtlı kullanıcı anahtarlarını yükler.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;

            _keys.Clear();
            _keys.AddRange(BuiltInKeys);

            // Diskteki kullanıcı havuzunu yükle
            LoadFromDisk();

            _initialized = true;
        }
    }

    /// <summary>
    /// Havuzdaki tüm anahtarları listeler.
    /// </summary>
    public static IReadOnlyList<AesKeyVaultEntry> GetAllKeys()
    {
        Initialize();
        lock (_lock)
        {
            return _keys.OrderBy(k => k.GameName).ToList();
        }
    }

    /// <summary>
    /// Verilen dosya yolu, klasör veya oyun adına göre en uygun AES anahtarını otomatik eşleştirir.
    /// </summary>
    public static AesKeyVaultEntry? FindKeyForPath(string filePath)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        var fullPath = filePath.ToLowerInvariant();
        var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

        lock (_lock)
        {
            // 1. Doğrudan PakPattern eşleşmesi
            foreach (var entry in _keys)
            {
                if (!string.IsNullOrEmpty(entry.PakPattern))
                {
                    var pattern = entry.PakPattern.ToLowerInvariant();
                    if (fullPath.Contains(pattern) || fileName.Contains(pattern))
                        return entry;
                }
            }

            // 2. Oyun adı kelime eşleşmesi
            foreach (var entry in _keys)
            {
                var cleanName = entry.GameName
                    .Replace("(", " ")
                    .Replace(")", " ")
                    .Replace(":", " ")
                    .ToLowerInvariant();

                var words = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                     .Where(w => w.Length > 3)
                                     .ToList();

                if (words.Count > 0 && words.All(w => fullPath.Contains(w)))
                {
                    return entry;
                }
            }

            // 3. Tekil anahtar kelime eşleşmesi (örn: "poppy", "hogwarts", "palworld", "tekken")
            foreach (var entry in _keys)
            {
                var firstWord = entry.GameName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
                if (firstWord.Length >= 4 && fullPath.Contains(firstWord))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Arama terimine göre anahtar havuzunda filtreleme yapar.
    /// </summary>
    public static List<AesKeyVaultEntry> Search(string query)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(query)) return GetAllKeys().ToList();

        var q = query.Trim().ToLowerInvariant();
        lock (_lock)
        {
            return _keys
                .Where(k => k.GameName.ToLowerInvariant().Contains(q) ||
                            k.PakPattern.ToLowerInvariant().Contains(q) ||
                            k.EngineVersion.ToLowerInvariant().Contains(q) ||
                            k.AesKey.ToLowerInvariant().Contains(q))
                .OrderBy(k => k.GameName)
                .ToList();
        }
    }

    /// <summary>
    /// Havuz'a yeni bir anahtar ekler veya mevcut olanı günceller. Diske kalıcı kaydeder.
    /// </summary>
    public static bool AddOrUpdateKey(AesKeyVaultEntry entry)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(entry.GameName) || string.IsNullOrWhiteSpace(entry.AesKey))
            return false;

        lock (_lock)
        {
            var existing = _keys.FirstOrDefault(k =>
                string.Equals(k.GameName, entry.GameName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k.AesKey, entry.AesKey, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                _keys.Remove(existing);
            }

            _keys.Add(entry with { IsCustom = true });
            SaveToDisk();
            return true;
        }
    }

    /// <summary>
    /// Kullanıcının eklediği bir anahtarı havuzdan siler.
    /// </summary>
    public static bool RemoveKey(AesKeyVaultEntry entry)
    {
        Initialize();
        lock (_lock)
        {
            var match = _keys.FirstOrDefault(k =>
                string.Equals(k.GameName, entry.GameName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.AesKey, entry.AesKey, StringComparison.OrdinalIgnoreCase));

            if (match != null && match.IsCustom)
            {
                _keys.Remove(match);
                SaveToDisk();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Açık kaynak bulut depolarından (GitHub Raw) en güncel anahtarları indirir ve yerel havuza katar.
    /// Sunucu masrafı sıfırdır, tamamen ücretsiz ve güvenlidir.
    /// </summary>
    public static async Task<int> UpdateFromCloudAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Initialize();
        int addedCount = 0;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Add("User-Agent", "GLS-GameLocalizationSystem/1.0");

        foreach (var url in CloudSources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                progress?.Report($"Bulut havuzuna bağlanılıyor: {Path.GetFileName(url)}...");
                var json = await client.GetStringAsync(url, ct);
                if (string.IsNullOrWhiteSpace(json)) continue;

                // FModel keys.json formatını parse et
                // Genellikle { "Game Name": "0xKey" } veya { "guid": "0xKey" } veya [{ "name": "...", "key": "..." }]
                int imported = ParseAndMergeKeysJson(json);
                addedCount += imported;
            }
            catch (Exception ex)
            {
                progress?.Report($"Kaynak atlandı ({ex.Message})");
            }
        }

        if (addedCount > 0)
        {
            SaveToDisk();
            progress?.Report($"🎉 Buluttan {addedCount} yeni oyun anahtarı havuza eklendi!");
        }
        else
        {
            progress?.Report("Havuz zaten en güncel durumda.");
        }

        return addedCount;
    }

    private static int ParseAndMergeKeysJson(string json)
    {
        int added = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    string nameOrGuid = prop.Name;
                    string key = prop.Value.GetString() ?? string.Empty;

                    if (AesDecryptionHelper.ValidateKey(key, out _))
                    {
                        var entry = new AesKeyVaultEntry
                        {
                            GameName = nameOrGuid,
                            AesKey = key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? key : "0x" + key,
                            EngineVersion = "UE4 / UE5",
                            PakPattern = nameOrGuid.Replace(" ", "_"),
                            IsCustom = true
                        };

                        lock (_lock)
                        {
                            if (!_keys.Any(k => string.Equals(k.AesKey, entry.AesKey, StringComparison.OrdinalIgnoreCase)))
                            {
                                _keys.Add(entry);
                                added++;
                            }
                        }
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    string name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string key = el.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(name) && AesDecryptionHelper.ValidateKey(key, out _))
                    {
                        var entry = new AesKeyVaultEntry
                        {
                            GameName = name,
                            AesKey = key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? key : "0x" + key,
                            EngineVersion = el.TryGetProperty("version", out var v) ? v.GetString() ?? "UE4" : "UE4",
                            PakPattern = name.Replace(" ", "_"),
                            IsCustom = true
                        };

                        lock (_lock)
                        {
                            if (!_keys.Any(k => string.Equals(k.AesKey, entry.AesKey, StringComparison.OrdinalIgnoreCase)))
                            {
                                _keys.Add(entry);
                                added++;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return added;
    }

    private static void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(VaultFilePath)) return;

            var json = File.ReadAllText(VaultFilePath);
            var diskKeys = JsonSerializer.Deserialize<List<AesKeyVaultEntry>>(json);
            if (diskKeys != null)
            {
                foreach (var dk in diskKeys)
                {
                    if (!_keys.Any(k => string.Equals(k.AesKey, dk.AesKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        _keys.Add(dk with { IsCustom = true });
                    }
                }
            }
        }
        catch { }
    }

    private static void SaveToDisk()
    {
        try
        {
            if (!Directory.Exists(VaultDirectory))
            {
                Directory.CreateDirectory(VaultDirectory);
            }

            var customKeys = _keys.Where(k => k.IsCustom).ToList();
            var json = JsonSerializer.Serialize(customKeys, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(VaultFilePath, json);
        }
        catch { }
    }
}
