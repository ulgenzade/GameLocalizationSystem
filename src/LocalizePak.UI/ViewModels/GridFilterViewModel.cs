using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalizePak.UI.ViewModels;

/// <summary>
/// Veri tablosu arama, filtreleme ve bul & değiştir ViewModel'i (Arama, durum filtreleri, sayfalama, sıralama).
/// </summary>
public sealed partial class GridFilterViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showOnlyErrors;

    [ObservableProperty]
    private string _selectedStatusFilter = "Tümü";

    public static IReadOnlyList<string> StatusFilterOptions { get; } =
        ["Tümü", "Bekleyenler (Pending)", "Tamamlananlar", "Hatalar", "Manuel Düzenlenenler", "Taşma Riski (%40+)", "QA / Etiket Hataları"];

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

    [ObservableProperty]
    private string _findResultInfo = string.Empty;
}
