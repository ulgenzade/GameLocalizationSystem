using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalizePak.Core.Models;

namespace LocalizePak.UI.ViewModels;

/// <summary>
/// Arşiv ve dosya çalışma alanı ViewModel'i (Paket açma, çıkarma, dosya ağacı ve sekmeler).
/// </summary>
public sealed partial class ArchiveWorkspaceViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<PakEntry> _pakEntries = [];

    [ObservableProperty]
    private PakEntry? _selectedPakEntry;

    [ObservableProperty]
    private int _selectedArchiveFilterIndex;

    [ObservableProperty]
    private string _loadedPakFileName = string.Empty;

    [ObservableProperty]
    private string _loadedPakSizeInfo = string.Empty;

    [ObservableProperty]
    private bool _isPakLoaded;

    [ObservableProperty]
    private string _archiveStatusText = "Açık arşiv yok";

    [ObservableProperty]
    private string _lastOpenedPath = string.Empty;

    public bool HasLocalizationFiles => IsPakLoaded && PakEntries.Count > 0;
    public bool IsEmptyArchive => IsPakLoaded && PakEntries.Count == 0;
}
