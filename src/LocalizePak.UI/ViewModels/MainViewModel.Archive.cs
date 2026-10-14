using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO.Services;

namespace LocalizePak.UI.ViewModels;

public sealed partial class MainViewModel
{
    // ─── Arşiv ve Dosya Yönetimi Durumu ─────────────────────────────────────

    private PakArchive? _currentArchive;
    private LocresFile? _currentLocresFile;
    private readonly Dictionary<string, LocresFile> _archiveLocresCache = new(StringComparer.OrdinalIgnoreCase);
    public const string AllFilesVirtualPath = "🌐 [TÜM ARŞİV] — Tüm Dosyaları Birleştir ve Göster";

    [ObservableProperty]
    private ObservableCollection<PakEntry> _pakEntries = [];

    [ObservableProperty]
    private PakEntry? _selectedPakEntry;

    [ObservableProperty]
    private int _selectedArchiveFilterIndex = 0; // 0: Oyun Dilleri, 1: Motor Dosyaları, 2: Tümü

    public ObservableCollection<PakEntry> FilteredPakEntries
    {
        get
        {
            if (PakEntries == null || PakEntries.Count == 0)
                return [];

            if (SelectedArchiveFilterIndex == 0)
            {
                // 0: Sadece Oyun Dilleri (Game.locres vb. - Engine.locres motor çöpleri hariç)
                var gameEntries = PakEntries.Where(e => e.IsGameFile).ToList();
                return gameEntries.Count > 0 ? new ObservableCollection<PakEntry>(gameEntries) : PakEntries;
            }
            else if (SelectedArchiveFilterIndex == 1)
            {
                // 1: Sadece Motor Dosyaları (Engine.locres)
                var engineEntries = PakEntries.Where(e => e.IsEngineFile).ToList();
                return new ObservableCollection<PakEntry>(engineEntries);
            }

            // 2: Tümü
            return PakEntries;
        }
    }

    [ObservableProperty]
    private string _loadedPakFileName = string.Empty;

    [ObservableProperty]
    private string _loadedPakSizeInfo = string.Empty;

    [ObservableProperty]
    private bool _isPakLoaded;

    public bool HasLocalizationFiles => IsPakLoaded && PakEntries.Count > 0;
    public bool IsEmptyArchive => IsPakLoaded && PakEntries.Count == 0;

    partial void OnIsPakLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasLocalizationFiles));
        OnPropertyChanged(nameof(IsEmptyArchive));
    }

    [ObservableProperty]
    private string _archiveStatusText = "Açık arşiv yok";

    [ObservableProperty]
    private string _lastOpenedPath = string.Empty;

    private void RefreshArchiveStatus()
    {
        OnPropertyChanged(nameof(HasLocalizationFiles));
        OnPropertyChanged(nameof(IsEmptyArchive));

        if (!IsPakLoaded || string.IsNullOrEmpty(LoadedPakFileName))
        {
            ArchiveStatusText = "Açık arşiv yok";
            return;
        }

        var count = PakEntries.Count(e => e.VirtualPath != AllFilesVirtualPath);
        ArchiveStatusText = $"{LoadedPakFileName} ({LoadedPakSizeInfo}) — {count} lokalizasyon dosyası";
    }

    partial void OnPakEntriesChanged(ObservableCollection<PakEntry> value)
    {
        OnPropertyChanged(nameof(FilteredPakEntries));
    }

    partial void OnSelectedArchiveFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(FilteredPakEntries));
        if (FilteredPakEntries.Count > 0 && (SelectedPakEntry == null || !FilteredPakEntries.Contains(SelectedPakEntry)))
        {
            SelectedPakEntry = FilteredPakEntries[0];
        }
    }

    partial void OnSelectedPakEntryChanged(PakEntry? value)
    {
        if (value is not null)
        {
            // Eğer dosyanın dili algılandıysa Kaynak Dili otomatik eşleştir
            var code = value.DetectedLanguageCode;
            if (!string.IsNullOrEmpty(code))
            {
                var matched = MapCodeToSourceLanguage(code);
                if (!string.IsNullOrEmpty(matched) && SourceLanguages.Contains(matched))
                {
                    SelectedSourceLanguage = matched;
                }
            }

            _ = Task.Run(async () => await ExtractLocresAsync(value));
        }
    }

    private static string MapCodeToSourceLanguage(string code)
    {
        var clean = code.ToLowerInvariant();
        return clean switch
        {
            "en" or "en-us" or "en-gb" => "İngilizce",
            "ja" or "ja-jp" => "Japonca",
            "zh" or "zh-hans" or "zh-cn" or "zh-hant" or "zh-tw" => "Çince",
            "ko" or "ko-kr" => "Korece",
            "de" or "de-de" => "Almanca",
            "fr" or "fr-fr" => "Fransızca",
            "es" or "es-es" or "es-419" or "es-mx" => "İspanyolca",
            "ru" or "ru-ru" => "Rusça",
            "tr" or "tr-tr" => "Türkçe",
            _ => "Otomatik Algıla"
        };
    }

    // ─── Arşiv ve Dosya Komutları ───────────────────────────────────────────

    [RelayCommand]
    private async Task OpenPakAsync()
    {
        var path = await ShowOpenFileDialogAsync(
            "PAK Arşivi Seç (.pak)",
            [
                ["PAK Arşivleri (*.pak)", "*.pak"],
                ["Tüm Dosyalar (*.*)", "*.*"]
            ]);
        if (path is null) return;
        await OpenPakFromPathAsync(path);
    }

    /// <summary>
    /// Verilen dosya yolundan .pak arşivini açar (Sürükle-Bırak veya Dosya Seçici).
    /// </summary>
    public async Task OpenPakFromPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            IsLoading = true;
            LastOpenedPath = path;
            StatusMessage = "Arşiv açılıyor...";
            _archiveLocresCache.Clear();

            // Eğer AES anahtarı boşsa, AES Anahtar Havuzumuzdan (Key Vault) dosya/klasör yoluna göre otomatik tespit et
            if (string.IsNullOrWhiteSpace(AesKey))
            {
                var match = AesKeyVaultService.FindKeyForPath(path);
                if (match != null && !string.IsNullOrWhiteSpace(match.AesKey))
                {
                    AesKey = match.AesKey;
                    StatusMessage = $"🔑 Anahtar Havuzundan '{match.GameName}' şifresi otomatik eşleşti: {match.AesKey[..Math.Min(16, match.AesKey.Length)]}...";
                }
            }

            await Task.Run(async () =>
            {
                var archive = await _pakService.OpenAsync(path,
                    string.IsNullOrWhiteSpace(AesKey) ? null : AesKey);

                _currentArchive = archive;

                var entryList = new List<PakEntry>();
                if (_currentArchive.LocalizationEntries.Count > 1)
                {
                    entryList.Add(new PakEntry { VirtualPath = AllFilesVirtualPath });
                }
                entryList.AddRange(_currentArchive.LocalizationEntries);

                var fi = new FileInfo(path);
                var sizeInfo = fi.Exists ? $"{(fi.Length / (1024.0 * 1024.0)):F1} MB" : string.Empty;

                Dispatcher.UIThread.Post(() =>
                {
                    PakEntries = new ObservableCollection<PakEntry>(entryList);
                    LoadedPakFileName = _currentArchive.FileName;
                    LoadedPakSizeInfo = sizeInfo;
                    IsPakLoaded = true;
                    RefreshArchiveStatus();

                    if (entryList.Count == 0)
                    {
                        LocalizationEntries.Clear();
                        StatusMessage = $"⚠️ '{_currentArchive.FileName}' açıldı ({LoadedPakSizeInfo}), ancak içinde çevrilebilir metin/lokalizasyon dosyası (.locres, .xml vb.) bulunamadı ({_currentArchive.Entries.Count} genel varlık/asset içeriyor).";
                        IsLoading = false;
                    }
                    else
                    {
                        OnPropertyChanged(nameof(FilteredPakEntries));

                        // 1. Öncelik: Oyun dosyaları (Game.locres vb.) içinde İngilizce (en) olan dosya!
                        // 2. Öncelik: Herhangi bir oyun dosyası (Game.locres vb.)
                        // 3. Öncelik: Listenin ilk dosyası
                        var preferredEntry = PakEntries.FirstOrDefault(e =>
                            e.IsGameFile && e.DetectedLanguageCode.Equals("en", StringComparison.OrdinalIgnoreCase))
                            ?? PakEntries.FirstOrDefault(e => e.IsGameFile)
                            ?? PakEntries.FirstOrDefault();

                        if (preferredEntry != null)
                        {
                            SelectedPakEntry = preferredEntry;
                        }
                        else if (PakEntries.Count > 0)
                        {
                            SelectedPakEntry = PakEntries[0];
                        }
                        else
                        {
                            IsLoading = false;
                        }
                    }
                });
            });
        }
        catch (Exception ex)
        {
            IsPakLoaded = false;
            if (string.IsNullOrWhiteSpace(AesKey) && (ex.Message.Contains("şifre", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("key", StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = $"Hata: Arşiv şifreli olabilir. AES Anahtar Havuzunu (Key Vault) veya Canlı RAM Çıkarıcı'yı kullanın.";
            }
            else
            {
                StatusMessage = $"Hata: {ex.Message}";
            }
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExtractLocresAsync(PakEntry? entry)
    {
        if (entry is null) return;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = true;
                StatusMessage = $"Lokalizasyon dosyası okunuyor: {Path.GetFileName(entry.VirtualPath)}...";
            });

            await Task.Run(async () =>
            {
                LocresFile? targetLocres = null;

                if (entry.VirtualPath == AllFilesVirtualPath)
                {
                    if (_currentArchive != null)
                    {
                        // Tüm dosyaları belleğe yükle ve birleştir
                        var masterLocres = new LocresFile { Version = LocresVersion.V3, SourcePath = AllFilesVirtualPath };
                        foreach (var realEntry in _currentArchive.LocalizationEntries)
                        {
                            try
                            {
                                if (!_archiveLocresCache.TryGetValue(realEntry.VirtualPath, out var subLocres))
                                {
                                    var stream = await _pakService.ExtractEntryAsync(_currentArchive, realEntry);
                                    subLocres = await _locresService.DeserializeAsync(stream);
                                    subLocres.SourcePath = realEntry.VirtualPath;
                                    _archiveLocresCache[realEntry.VirtualPath] = subLocres;
                                }

                                var nsName = Path.GetFileName(realEntry.VirtualPath);
                                var ns = new LocresNamespace { Name = nsName };
                                foreach (var e in subLocres.AllEntries)
                                {
                                    e.NamespaceName = nsName;
                                    ns.Entries.Add(e);
                                }
                                masterLocres.Namespaces.Add(ns);
                            }
                            catch
                            {
                                // Geçersiz veya ikili dosya ise birleştirmeden güvenle atla
                            }
                        }

                        targetLocres = masterLocres;
                    }
                }
                else
                {
                    if (!_archiveLocresCache.TryGetValue(entry.VirtualPath, out var subLocres))
                    {
                        if (File.Exists(entry.VirtualPath))
                        {
                            await using var fs = File.OpenRead(entry.VirtualPath);
                            subLocres = await _locresService.DeserializeAsync(fs);
                            subLocres.SourcePath = entry.VirtualPath;
                            _archiveLocresCache[entry.VirtualPath] = subLocres;
                        }
                        else if (_currentArchive != null)
                        {
                            var stream = await _pakService.ExtractEntryAsync(_currentArchive, entry);
                            subLocres = await _locresService.DeserializeAsync(stream);
                            subLocres.SourcePath = entry.VirtualPath;
                            _archiveLocresCache[entry.VirtualPath] = subLocres;
                        }
                    }
                    targetLocres = subLocres;
                }

                if (targetLocres is null)
                {
                    Dispatcher.UIThread.Post(() => { IsLoading = false; });
                    return;
                }

                _currentLocresFile = targetLocres;

                var viewModels = _currentLocresFile.AllEntries
                    .Select(e => new LocalizationEntryViewModel(e))
                    .ToList();

                var total = viewModels.Count;
                var done = viewModels.Count(v => v.Status == TranslationStatus.Done || v.Status == TranslationStatus.ManuallyEdited);
                var err = viewModels.Count(v => v.Status == TranslationStatus.Error);
                var obsCollection = new ObservableCollection<LocalizationEntryViewModel>(viewModels);

                Dispatcher.UIThread.Post(() =>
                {
                    LocalizationEntries = obsCollection;
                    TotalEntries = total;
                    CompletedEntries = done;
                    ErrorCount = err;
                    ApplyFilter();
                    RefreshStats();
                    StatusMessage = $"Yüklendi: {entry.VirtualPath} — {TotalEntries} girdi ({CompletedEntries} tamamlandı, {ErrorCount} hata)";
                    IsLoading = false;
                });
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = $"Lokalizasyon dosyası açılamadı: {ex.Message}";
                IsLoading = false;
            });
        }
    }

    [RelayCommand]
    public async Task OpenSingleFileAsync()
    {
        var path = await ShowOpenFileDialogAsync(
            "Lokalizasyon veya Arşiv Dosyası Seç (.locres, .pak, .json, .csv, .xml)",
            [
                ["Tüm Desteklenen Dosyalar", "*.locres", "*.pak", "*.json", "*.csv", "*.tsv", "*.xml", "*.txt", "*.po"],
                ["Lokalizasyon Dosyaları (*.locres, *.json, ...)", "*.locres", "*.json", "*.csv", "*.tsv", "*.xml", "*.txt", "*.po"],
                ["PAK Arşivleri (*.pak)", "*.pak"],
                ["Tüm Dosyalar (*.*)", "*.*"]
            ]);

        if (path is null) return;
        await OpenSingleFileFromPathAsync(path);
    }

    public async Task OpenSingleFileFromPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        // Kullanıcı bu diyalogdan .pak seçtiyse otomatik olarak .pak yükleyicisine yönlendir
        if (Path.GetExtension(path).Equals(".pak", StringComparison.OrdinalIgnoreCase))
        {
            await OpenPakFromPathAsync(path);
            return;
        }

        try
        {
            IsLoading = true;
            LastOpenedPath = path;
            StatusMessage = $"Dosya açılıyor: {Path.GetFileName(path)}...";

            await Task.Run(async () =>
            {
                await using var fs = File.OpenRead(path);
                var locres = await _locresService.DeserializeAsync(fs);
                locres.SourcePath = path;

                _currentLocresFile = locres;
                _currentArchive = null; // Tekil dosya modu

                var viewModels = _currentLocresFile.AllEntries
                    .Select(e => new LocalizationEntryViewModel(e))
                    .ToList();

                var total = viewModels.Count;
                var done = viewModels.Count(v => v.Status == TranslationStatus.Done || v.Status == TranslationStatus.ManuallyEdited);
                var err = viewModels.Count(v => v.Status == TranslationStatus.Error);
                var obsCollection = new ObservableCollection<LocalizationEntryViewModel>(viewModels);

                var fi = new FileInfo(path);
                var sizeInfo = $"{(fi.Length / 1024.0):F1} KB";

                Dispatcher.UIThread.Post(() =>
                {
                    PakEntries = new ObservableCollection<PakEntry>([new PakEntry { VirtualPath = path }]);
                    LoadedPakFileName = Path.GetFileName(path);
                    LoadedPakSizeInfo = sizeInfo;
                    IsPakLoaded = true;
                    RefreshArchiveStatus();
                    LocalizationEntries = obsCollection;
                    TotalEntries = total;
                    CompletedEntries = done;
                    ErrorCount = err;
                    ApplyFilter();
                    StatusMessage = $"Yüklendi: {Path.GetFileName(path)} — {TotalEntries} girdi ({CompletedEntries} tamamlandı, {ErrorCount} hata)";
                    IsLoading = false;
                });
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = $"Dosya açılamadı: {ex.Message}";
                IsLoading = false;
            });
        }
    }

    [RelayCommand]
    public void CloseCurrentFile()
    {
        _currentArchive = null;
        _currentLocresFile = null;
        _archiveLocresCache.Clear();
        PakEntries.Clear();
        LocalizationEntries.Clear();
        FilteredEntries.Clear();
        SelectedPakEntry = null;
        IsPakLoaded = false;
        IsLoading = false;
        RefreshArchiveStatus();
        LoadedPakFileName = string.Empty;
        LoadedPakSizeInfo = string.Empty;
        LastOpenedPath = string.Empty;
        TotalEntries = 0;
        CompletedEntries = 0;
        ErrorCount = 0;
        StatusMessage = "Dosya kapatıldı. Yeni bir .pak veya lokalizasyon dosyası açabilirsiniz.";
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task AddExternalFileAsync()
    {
        var path = await ShowOpenFileDialogAsync(
            "Dosya veya Arşiv Ekle (.pak, .locres, .json, .csv, .xml, .po)",
            [
                ["Tüm Desteklenen Dosyalar (*.pak, *.locres, ...)", "*.pak", "*.locres", "*.json", "*.csv", "*.tsv", "*.xml", "*.po", "*.txt"],
                ["PAK Arşivleri (*.pak)", "*.pak"],
                ["Lokalizasyon Dosyaları (*.locres, *.json, ...)", "*.locres", "*.json", "*.csv", "*.tsv", "*.xml", "*.po", "*.txt"],
                ["Tüm Dosyalar (*.*)", "*.*"]
            ]);

        if (path is null) return;
        await AddExternalFileFromPathAsync(path);
    }

    public async Task AddExternalFileFromPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        // Kullanıcı bu diyalogdan .pak seçtiyse otomatik olarak .pak yükleyicisine yönlendir
        if (Path.GetExtension(path).Equals(".pak", StringComparison.OrdinalIgnoreCase))
        {
            await OpenPakFromPathAsync(path);
            return;
        }

        if (!IsPakLoaded || PakEntries.Count == 0)
        {
            await OpenSingleFileFromPathAsync(path);
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Harici dosya ekleniyor: {Path.GetFileName(path)}...";

            await Task.Run(async () =>
            {
                await using var fs = File.OpenRead(path);
                var locres = await _locresService.DeserializeAsync(fs);
                locres.SourcePath = path;

                var entryName = Path.GetFileName(path);
                _archiveLocresCache[path] = locres;

                var newEntry = new PakEntry
                {
                    VirtualPath = path,
                    UncompressedSize = new FileInfo(path).Length
                };

                Dispatcher.UIThread.Post(() =>
                {
                    PakEntries.Add(newEntry);
                    SelectedPakEntry = newEntry;
                    RefreshArchiveStatus();
                    StatusMessage = $"Dosya başarıyla eklendi: {entryName} ({locres.TotalEntryCount} satır)";
                    IsLoading = false;
                });
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = $"Dosya eklenemedi: {ex.Message}";
                IsLoading = false;
            });
        }
    }

    [RelayCommand]
    public void RemoveSelectedArchiveFile()
    {
        if (SelectedPakEntry is null)
        {
            StatusMessage = "Lütfen önce listeden kaldırılacak dosyayı seçin.";
            return;
        }

        var toRemove = SelectedPakEntry;
        var fileName = Path.GetFileName(toRemove.VirtualPath);

        PakEntries.Remove(toRemove);
        _archiveLocresCache.Remove(toRemove.VirtualPath, out _);

        if (_currentArchive != null)
        {
            _currentArchive.Entries = _currentArchive.Entries.Where(e => e != toRemove).ToList();
        }

        RefreshArchiveStatus();

        if (PakEntries.Count > 0)
        {
            SelectedPakEntry = PakEntries[0];
            StatusMessage = $"'{fileName}' listeden kaldırıldı. Yeni seçili dosya yüklendi.";
        }
        else
        {
            CloseCurrentFile();
            StatusMessage = "Son dosya da kaldırıldı. Liste temizlendi.";
        }
    }
}
