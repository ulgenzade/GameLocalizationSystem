using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalizePak.UI.ViewModels;

/// <summary>
/// Yama derleme ve dışa aktarma ViewModel'i (Yama derleme, _P.pak, doğrudan kayıt, yedekleme).
/// </summary>
public sealed partial class ExportPatchViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _lastExportedDirectory;

    [ObservableProperty]
    private bool _hasExportedDirectory;

    [ObservableProperty]
    private bool _hasRecoverySession;

    [ObservableProperty]
    private string _recoverySessionInfo = string.Empty;
}
