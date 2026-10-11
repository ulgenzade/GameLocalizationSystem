using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocalizePak.Translation
{
    /// <summary>
    /// Thread-safe çeviri belleği (Translation Memory).
    /// Kaynak metin ve hedef dil bazında yerel JSON dosyasında önbelleğe alır.
    /// Eşzamanlı arka plan iş parçacıkları için ConcurrentDictionary ile korunur.
    /// </summary>
    public class TranslationMemory
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<(string Source, string TargetLang), string> _cache = new();
        private readonly object _fileLock = new();
        private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);

        public TranslationMemory(string? folderPath = null)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                folderPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GameLocalizationSystem");
            }

            Directory.CreateDirectory(folderPath);
            _filePath = Path.Combine(folderPath, "translation_memory.json");
            LoadFromDisk();
        }

        private void LoadFromDisk()
        {
            try
            {
                lock (_fileLock)
                {
                    if (!File.Exists(_filePath))
                        return;

                    var json = File.ReadAllText(_filePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var entries = JsonSerializer.Deserialize<List<MemoryEntry>>(json, options);
                    if (entries != null)
                    {
                        foreach (var e in entries)
                        {
                            if (!string.IsNullOrEmpty(e.Source) && !string.IsNullOrEmpty(e.TargetLang) && !string.IsNullOrEmpty(e.Translation))
                            {
                                _cache[(e.Source, e.TargetLang)] = e.Translation;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Okuma hatasında boş önbellekle güvenle devam et
            }
        }

        /// <summary>
        /// Belleği diskten asenkron olarak yeniden yükler.
        /// </summary>
        public async Task LoadAsync()
        {
            if (!File.Exists(_filePath))
            {
                _cache.Clear();
                return;
            }

            try
            {
                string json;
                lock (_fileLock)
                {
                    json = File.ReadAllText(_filePath);
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var entries = JsonSerializer.Deserialize<List<MemoryEntry>>(json, options) ?? new List<MemoryEntry>();
                _cache.Clear();
                foreach (var e in entries)
                {
                    if (!string.IsNullOrEmpty(e.Source) && !string.IsNullOrEmpty(e.TargetLang) && !string.IsNullOrEmpty(e.Translation))
                    {
                        _cache[(e.Source, e.TargetLang)] = e.Translation;
                    }
                }
            }
            catch (Exception)
            {
                _cache.Clear();
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Belleği diske atomik ve güvenli bir şekilde kaydeder.
        /// </summary>
        public async Task SaveAsync()
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var entries = new List<MemoryEntry>(_cache.Count);
                foreach (var kvp in _cache)
                {
                    entries.Add(new MemoryEntry
                    {
                        Source = kvp.Key.Source,
                        TargetLang = kvp.Key.TargetLang,
                        Translation = kvp.Value
                    });
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(entries, options);

                var tempPath = _filePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);

                lock (_fileLock)
                {
                    File.Move(tempPath, _filePath, overwrite: true);
                }
            }
            catch (Exception)
            {
                // Uygulama çökmesini önlemek için hata yutulur
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// Verilen kaynak metin ve hedef dil için önbellekte kayıtlı çeviriyi arar.
        /// </summary>
        public bool TryGet(string source, string targetLang, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? translation)
        {
            return _cache.TryGetValue((source, targetLang), out translation);
        }

        /// <summary>
        /// Çeviriyi önbelleğe thread-safe şekilde ekler.
        /// </summary>
        public void Add(string source, string targetLang, string translation)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translation))
                return;

            _cache.TryAdd((source, targetLang), translation);
        }

        /// <summary>
        /// Tüm önbellek girdilerini temizler.
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        private class MemoryEntry
        {
            public string Source { get; set; } = string.Empty;
            public string TargetLang { get; set; } = string.Empty;
            public string Translation { get; set; } = string.Empty;
        }
    }
}