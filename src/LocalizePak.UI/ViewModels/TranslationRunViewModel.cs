using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalizePak.UI.ViewModels;

/// <summary>
/// Çeviri çalışma ve yapay zeka akışı ViewModel'i (AI motoru, batch akışı, buton durumları, iptal/devam).
/// </summary>
public sealed partial class TranslationRunViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isTranslating;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private double _translationProgress;

    [ObservableProperty]
    private string _elapsedTime = "00:00";

    [ObservableProperty]
    private string _estimatedTimeRemaining = "--:--";

    [ObservableProperty]
    private string _translationSpeed = "0 satır/sn";

    [ObservableProperty]
    private string _selectedProviderName = "Gemini";

    [ObservableProperty]
    private int _completedEntries;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _totalEntries;

    public int PendingEntries => Math.Max(0, TotalEntries - CompletedEntries - ErrorCount);
}
