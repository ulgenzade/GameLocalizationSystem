using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.IO.Services;

namespace LocalizePak.UI.ViewModels;

public sealed partial class RamAesExtractorViewModel : ObservableObject
{
    private readonly string? _pakFilePath;

    public ObservableCollection<RunningGameProcess> ProcessList { get; } = [];

    [ObservableProperty]
    private RunningGameProcess? _selectedProcess;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private int _scanProgress;

    [ObservableProperty]
    private string _statusMessage = "Lütfen listeden oyunu seçip 'Belleği Tara' butonuna basın.";

    [ObservableProperty]
    private string _foundAesKey = string.Empty;

    [ObservableProperty]
    private bool _hasFoundKey;

    public Action<string>? OnKeyApplied { get; set; }

    public RamAesExtractorViewModel(string? pakFilePath = null)
    {
        _pakFilePath = pakFilePath;
        RefreshProcesses();
    }

    [RelayCommand]
    public void RefreshProcesses()
    {
        ProcessList.Clear();
        var list = RamAesExtractor.GetRunningProcesses();

        var query = FilterText.Trim().ToLowerInvariant();
        foreach (var p in list)
        {
            if (string.IsNullOrEmpty(query) ||
                p.ProcessName.ToLowerInvariant().Contains(query) ||
                p.MainWindowTitle.ToLowerInvariant().Contains(query))
            {
                ProcessList.Add(p);
            }
        }

        if (ProcessList.Count > 0 && SelectedProcess is null)
        {
            SelectedProcess = ProcessList.FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle)) ?? ProcessList[0];
        }

        StatusMessage = $"{ProcessList.Count} çalışan süreç listelendi.";
    }

    partial void OnFilterTextChanged(string value)
    {
        RefreshProcesses();
    }

    [RelayCommand]
    public async Task StartScanAsync()
    {
        if (SelectedProcess is null)
        {
            StatusMessage = "Lütfen önce listeden çalışan oyunu seçin.";
            return;
        }

        try
        {
            IsScanning = true;
            HasFoundKey = false;
            FoundAesKey = string.Empty;
            ScanProgress = 0;
            StatusMessage = $"'{SelectedProcess.ProcessName}' süreci taranıyor, lütfen bekleyin...";

            var progress = new Progress<int>(p =>
            {
                ScanProgress = p;
                StatusMessage = $"RAM taranıyor: %{p}";
            });

            var key = await RamAesExtractor.ScanProcessForAesKeyAsync(
                SelectedProcess.Id,
                _pakFilePath,
                progress);

            if (!string.IsNullOrEmpty(key))
            {
                FoundAesKey = key;
                HasFoundKey = true;
                StatusMessage = "🎉 AES-256 Anahtarı RAM'den Başarıyla Çıkarıldı!";
            }
            else
            {
                StatusMessage = "⚠️ Bellekte eşleşen kesin bir AES anahtarı bulunamadı. Oyunun ana menüsüne kadar ilerlediğinden emin olun.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public void ApplyKey()
    {
        if (!string.IsNullOrEmpty(FoundAesKey))
        {
            OnKeyApplied?.Invoke(FoundAesKey);
        }
    }
}
