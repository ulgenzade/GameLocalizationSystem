using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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

    public int PendingEntries => Math.Max(0, TotalEntries - CompletedEntries - ErrorCount);
    public int ManuallyEditedCount => LocalizationEntries.Count(e => e.Status == TranslationStatus.ManuallyEdited);
    public int QaIssueCount => LocalizationEntries.Count(e => e.HasQaIssue);
    public int OverflowRiskCount => LocalizationEntries.Count(e => e.IsOverflowRisk);
    public double CompletionPercentage => TotalEntries == 0 ? 0 : Math.Round((double)CompletedEntries / TotalEntries * 100, 1);
    public int TotalSourceWords => LocalizationEntries.Sum(e => e.SourceText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    public int TotalSourceChars => LocalizationEntries.Sum(e => e.SourceText.Length);

    public void RefreshStats()
    {
        OnPropertyChanged(nameof(PendingEntries));
        OnPropertyChanged(nameof(ManuallyEditedCount));
        OnPropertyChanged(nameof(QaIssueCount));
        OnPropertyChanged(nameof(OverflowRiskCount));
        OnPropertyChanged(nameof(CompletionPercentage));
        OnPropertyChanged(nameof(TotalSourceWords));
        OnPropertyChanged(nameof(TotalSourceChars));
        RefreshTranslationState();
    }

    partial void OnTotalEntriesChanged(int value) => RefreshStats();
    partial void OnCompletedEntriesChanged(int value) => RefreshStats();
    partial void OnErrorCountChanged(int value) => RefreshStats();

    partial void OnFilterTextChanged(string value) => ApplyFilter();
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
