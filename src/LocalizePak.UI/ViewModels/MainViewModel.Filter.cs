using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.Core.Enums;
using LocalizePak.Translation.Services;

namespace LocalizePak.UI.ViewModels;

public sealed partial class MainViewModel
{
    // ─── Veri Tablosu ve Filtreleme Durumu ───────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<LocalizationEntryViewModel> _localizationEntries = [];

    [ObservableProperty]
    private ObservableCollection<LocalizationEntryViewModel> _filteredEntries = [];

    [ObservableProperty]
    private LocalizationEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showOnlyErrors;

    public static IReadOnlyList<string> StatusFilterOptions { get; } =
        ["Tümü", "Bekleyenler (Pending)", "Tamamlananlar", "Hatalar", "Manuel Düzenlenenler", "Taşma Riski (%40+)", "QA / Etiket Hataları"];

    [ObservableProperty]
    private string _selectedStatusFilter = "Tümü";

    // ─── İstatistikler ve Metrikler ─────────────────────────────────────────

    [ObservableProperty]
    private int _totalEntries;

    [ObservableProperty]
    private int _completedEntries;

    [ObservableProperty]
    private int _errorCount;

    // İstatistikler artık her bağlama okumasında yeniden hesaplanmıyor: tek geçişte hesaplanıp önbelleğe alınıyor.
    // (QaIssueCount arayüzde 3 yerde bağlı; eskiden her ilerleme raporunda 22.000 satırda regex QA 3 kez koşuyordu.)
    private int _manuallyEditedCount;
    private int _qaIssueCount;
    private int _overflowRiskCount;
    private int _totalSourceWords;
    private int _totalSourceChars;
    private ObservableCollection<LocalizationEntryViewModel>? _sourceStatsFor;
    private int _sourceStatsCount = -1;

    public int PendingEntries => Math.Max(0, TotalEntries - CompletedEntries - ErrorCount);
    public int ManuallyEditedCount => _manuallyEditedCount;
    public int QaIssueCount => _qaIssueCount;
    public int OverflowRiskCount => _overflowRiskCount;
    public double CompletionPercentage => TotalEntries == 0 ? 0 : Math.Round((double)CompletedEntries / TotalEntries * 100, 1);
    public int TotalSourceWords => _totalSourceWords;
    public int TotalSourceChars => _totalSourceChars;

    /// <summary>İstatistikleri tek geçişte yeniden hesaplar ve arayüze bildirir.</summary>
    public void RefreshStats()
    {
        _statsRefreshPending = false;
        RecomputeStats();

        OnPropertyChanged(nameof(PendingEntries));
        OnPropertyChanged(nameof(ManuallyEditedCount));
        OnPropertyChanged(nameof(QaIssueCount));
        OnPropertyChanged(nameof(OverflowRiskCount));
        OnPropertyChanged(nameof(CompletionPercentage));
        OnPropertyChanged(nameof(TotalSourceWords));
        OnPropertyChanged(nameof(TotalSourceChars));
        RefreshTranslationState();
    }

    private void RecomputeStats()
    {
        int manual = 0, qa = 0, overflow = 0;
        foreach (var e in LocalizationEntries)
        {
            if (e.Status == TranslationStatus.ManuallyEdited) manual++;
            if (e.IsOverflowRisk) overflow++;
            if (e.HasQaIssue) qa++;
        }
        _manuallyEditedCount = manual;
        _qaIssueCount = qa;
        _overflowRiskCount = overflow;

        // Kaynak metinler çeviri sırasında değişmez; kelime/karakter sayısı yalnızca liste değişince hesaplanır.
        if (!ReferenceEquals(_sourceStatsFor, LocalizationEntries) || _sourceStatsCount != LocalizationEntries.Count)
        {
            int words = 0, chars = 0;
            foreach (var e in LocalizationEntries)
            {
                chars += e.SourceText.Length;
                words += CountWords(e.SourceText);
            }
            _totalSourceWords = words;
            _totalSourceChars = chars;
            _sourceStatsFor = LocalizationEntries;
            _sourceStatsCount = LocalizationEntries.Count;
        }
    }

    /// <summary>Boşlukla ayrılmış kelime sayısı (Split gibi dizi ayırmadan).</summary>
    public static int CountWords(string text)
    {
        int count = 0;
        bool inWord = false;
        foreach (var c in text)
        {
            if (c == ' ') inWord = false;
            else if (!inWord) { inWord = true; count++; }
        }
        return count;
    }

    // Çeviri sırasında her batch ilerlemesi CompletedEntries / ErrorCount'u değiştirir.
    // İstatistikler en fazla ~3 kez/sn hesaplanır; son değer her zaman yansıtılır.
    private bool _statsRefreshPending;
    private static readonly TimeSpan StatsRefreshInterval = TimeSpan.FromMilliseconds(300);

    private void ScheduleStatsRefresh()
    {
        if (_statsRefreshPending) return;
        _statsRefreshPending = true;
        _ = Task.Delay(StatsRefreshInterval).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_statsRefreshPending) RefreshStats();
            }), TaskScheduler.Default);
    }

    partial void OnTotalEntriesChanged(int value) => RefreshStats();
    partial void OnCompletedEntriesChanged(int value) => ScheduleStatsRefresh();
    partial void OnErrorCountChanged(int value) => ScheduleStatsRefresh();

    // Arama kutusu: her tuşta 22.000 satırı filtrelemek yerine yazma bitince (250 ms) bir kez filtrele.
    private CancellationTokenSource? _filterDebounceCts;
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(250);

    partial void OnFilterTextChanged(string value)
    {
        _filterDebounceCts?.Cancel();
        var cts = _filterDebounceCts = new CancellationTokenSource();
        _ = Task.Delay(FilterDebounce, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!cts.IsCancellationRequested) ApplyFilter();
                });
        }, TaskScheduler.Default);
    }

    partial void OnShowOnlyErrorsChanged(bool value) => ApplyFilter();
    partial void OnSelectedStatusFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = LocalizationEntries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filtered = filtered.Where(e =>
                e.Key.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                e.SourceText.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                e.TranslatedText.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
        }

        if (ShowOnlyErrors)
        {
            filtered = filtered.Where(e => e.Status == TranslationStatus.Error || e.HasQaIssue);
        }

        if (!string.IsNullOrEmpty(SelectedStatusFilter) && SelectedStatusFilter != "Tümü")
        {
            filtered = SelectedStatusFilter switch
            {
                "Bekleyenler (Pending)" => filtered.Where(e => e.Status == TranslationStatus.Pending),
                "Tamamlananlar" => filtered.Where(e => e.Status == TranslationStatus.Done),
                "Hatalar" => filtered.Where(e => e.Status == TranslationStatus.Error),
                "Manuel Düzenlenenler" => filtered.Where(e => e.Status == TranslationStatus.ManuallyEdited),
                "Taşma Riski (%40+)" => filtered.Where(e => e.IsOverflowRisk),
                "QA / Etiket Hataları" => filtered.Where(e => e.HasQaIssue),
                _ => filtered
            };
        }

        FilteredEntries = new ObservableCollection<LocalizationEntryViewModel>(filtered);

        // Satırları değiştiren komutların hepsi ApplyFilter çağırır; önbellekli istatistikleri de tazele (kısıtlı).
        ScheduleStatsRefresh();
    }

    /// <summary>Durum sayaçlarını (tamamlanan / hatalı) satırlardan yeniden sayar; elle durum değişikliklerinden sonra çağrılır.</summary>
    private void RecountStatuses()
    {
        int completed = 0, errors = 0;
        foreach (var e in LocalizationEntries)
        {
            if (e.Status is TranslationStatus.Done or TranslationStatus.ManuallyEdited) completed++;
            else if (e.Status == TranslationStatus.Error) errors++;
        }
        CompletedEntries = completed;
        ErrorCount = errors;
    }

    // ─── Toplu Bul ve Değiştir (Find & Replace) ─────────────────────────────

    [ObservableProperty]
    private bool _isFindReplaceOpen;

    [ObservableProperty]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _matchWholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _findScope = "Yalnızca Hedef Çeviri";

    public static IReadOnlyList<string> FindScopeOptions { get; } =
        ["Yalnızca Hedef Çeviri", "Yalnızca Kaynak Metin", "Her İkisi"];

    [ObservableProperty]
    private string _findResultInfo = string.Empty;

    [RelayCommand]
    public void ToggleFindReplace()
    {
        IsFindReplaceOpen = !IsFindReplaceOpen;
        if (IsFindReplaceOpen)
        {
            FindResultInfo = "Aranacak kelimeyi girin ve 'Tümünü Değiştir' veya 'Sonrakini Bul' butonuna tıklayın.";
        }
    }

    [RelayCommand]
    public void CloseFindReplace()
    {
        IsFindReplaceOpen = false;
    }

    [RelayCommand]
    public void ReplaceAll()
    {
        if (string.IsNullOrEmpty(FindText))
        {
            FindResultInfo = "Lütfen aranacak metni girin.";
            return;
        }

        int affectedEntries = 0;
        int totalReplacements = 0;

        RegexOptions regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        Regex? regex = null;

        if (UseRegex)
        {
            try
            {
                regex = new Regex(FindText, regexOptions);
            }
            catch (Exception ex)
            {
                FindResultInfo = $"Geçersiz Regex: {ex.Message}";
                return;
            }
        }
        else if (MatchWholeWord)
        {
            var pattern = $@"\b{Regex.Escape(FindText)}\b";
            regex = new Regex(pattern, regexOptions);
        }

        bool targetTranslation = FindScope is "Yalnızca Hedef Çeviri" or "Her İkisi";
        bool targetSource = FindScope is "Yalnızca Kaynak Metin" or "Her İkisi";

        foreach (var vm in LocalizationEntries)
        {
            bool modified = false;

            if (targetTranslation && !string.IsNullOrEmpty(vm.TranslatedText))
            {
                string newText;
                int count = 0;

                if (regex != null)
                {
                    count = regex.Matches(vm.TranslatedText).Count;
                    newText = regex.Replace(vm.TranslatedText, ReplaceText ?? string.Empty);
                }
                else
                {
                    var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    newText = ReplaceString(vm.TranslatedText, FindText, ReplaceText ?? string.Empty, comparison, out count);
                }

                if (count > 0 && newText != vm.TranslatedText)
                {
                    vm.TranslatedText = newText;
                    vm.Model.Status = TranslationStatus.ManuallyEdited;
                    vm.RefreshFromModel();
                    modified = true;
                    totalReplacements += count;
                }
            }

            if (modified)
            {
                affectedEntries++;
            }
        }

        ApplyFilter();
        FindResultInfo = $"✅ {affectedEntries} satırda toplam {totalReplacements} değişiklik yapıldı.";
        StatusMessage = $"Toplu değiştirme tamamlandı: {affectedEntries} satır güncellendi ({totalReplacements} eşleşme).";
    }

    [RelayCommand]
    public void FindNext()
    {
        if (string.IsNullOrEmpty(FindText))
        {
            FindResultInfo = "Lütfen aranacak metni girin.";
            return;
        }

        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int currentIndex = SelectedEntry != null ? FilteredEntries.IndexOf(SelectedEntry) : -1;
        int count = FilteredEntries.Count;

        if (count == 0)
        {
            FindResultInfo = "Tabloda taranacak satır yok.";
            return;
        }

        bool targetTranslation = FindScope is "Yalnızca Hedef Çeviri" or "Her İkisi";
        bool targetSource = FindScope is "Yalnızca Kaynak Metin" or "Her İkisi";

        for (int i = 1; i <= count; i++)
        {
            int checkIndex = (currentIndex + i) % count;
            var item = FilteredEntries[checkIndex];

            bool match = false;
            if (targetTranslation && item.TranslatedText?.IndexOf(FindText, comparison) >= 0)
                match = true;
            if (!match && targetSource && item.SourceText?.IndexOf(FindText, comparison) >= 0)
                match = true;

            if (match)
            {
                SelectedEntry = item;
                FindResultInfo = $"Bulundu (Satır {checkIndex + 1}/{count}): '{item.Key}'";
                return;
            }
        }

        FindResultInfo = $"Eşleşme bulunamadı: '{FindText}'";
    }

    private static string ReplaceString(string str, string oldValue, string newValue, StringComparison comparison, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(oldValue)) return str;

        var sb = new System.Text.StringBuilder();
        int previousIndex = 0;
        int index = str.IndexOf(oldValue, comparison);

        while (index != -1)
        {
            sb.Append(str.Substring(previousIndex, index - previousIndex));
            sb.Append(newValue);
            count++;
            previousIndex = index + oldValue.Length;
            index = str.IndexOf(oldValue, previousIndex, comparison);
        }

        sb.Append(str.Substring(previousIndex));
        return sb.ToString();
    }

    // ─── Otomatik Kalite Kontrol (QA Checker) ────────────────────────────────

    [RelayCommand]
    public void RunQaCheck()
    {
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Önce bir dosya açmalısınız.";
            return;
        }

        int totalIssues = 0;
        int affectedEntries = 0;

        foreach (var vm in LocalizationEntries)
        {
            var issues = TranslationQaService.ValidateEntry(vm.Model);
            if (issues.Count > 0)
            {
                affectedEntries++;
                totalIssues += issues.Count;
                vm.Model.HasVariableError = true;
                vm.Model.Notes = string.Join(" | ", issues.Select(i => i.Description));
                vm.RefreshFromModel();
            }
            else
            {
                vm.Model.HasVariableError = false;
                vm.RefreshFromModel();
            }
        }

        SelectedStatusFilter = "QA / Etiket Hataları";
        ApplyFilter();

        if (affectedEntries > 0)
        {
            StatusMessage = $"⚠️ Kalite Kontrolü: {affectedEntries} satırda toplam {totalIssues} etiket/değişken sorunu bulundu. 'Etiketleri Otomatik Onar' ile düzeltebilirsiniz.";
        }
        else
        {
            StatusMessage = "✅ Kalite Kontrolü Başarılı! Hiçbir eksik değişken veya kapanmayan etiket bulunamadı.";
        }
    }

    [RelayCommand]
    public void AutoFixAllQaIssues()
    {
        if (LocalizationEntries.Count == 0) return;

        int fixedCount = 0;
        foreach (var vm in LocalizationEntries)
        {
            if (TranslationQaService.AutoFixEntry(vm.Model))
            {
                vm.RefreshFromModel();
                fixedCount++;
            }
        }

        ApplyFilter();
        StatusMessage = fixedCount > 0
            ? $"✅ {fixedCount} satırdaki eksik etiketler ve değişkenler otomatik olarak onarıldı!"
            : "Onarılacak etiket veya değişken sorunu bulunamadı.";
    }

    // ─── Tablo Satır Seçim Komutları ─────────────────────────────────────────

    [RelayCommand]
    private void SelectAllFiltered()
    {
        foreach (var item in FilteredEntries)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in LocalizationEntries)
            item.IsSelected = false;
    }

    [RelayCommand]
    private void SelectOnlyErrors()
    {
        foreach (var item in LocalizationEntries)
            item.IsSelected = item.Status == TranslationStatus.Error;
    }
}
