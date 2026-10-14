using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.IO.Services;

namespace LocalizePak.UI.ViewModels;

public sealed partial class RamAesExtractorViewModel : ObservableObject
{
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<RunningGameProcess> ProcessList { get; } = [];

    [ObservableProperty]
    private string _pakFilePath = string.Empty;

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
        PakFilePath = pakFilePath ?? string.Empty;
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

    [ObservableProperty]
    private string _exeFilePath = string.Empty;

    partial void OnFilterTextChanged(string value)
    {
        RefreshProcesses();
    }

    partial void OnSelectedProcessChanged(RunningGameProcess? value)
    {
        if (value != null)
        {
            if (!string.IsNullOrEmpty(value.FilePath))
            {
                ExeFilePath = value.FilePath;
            }

            if (string.IsNullOrEmpty(PakFilePath))
            {
                var autoPak = RamAesExtractor.TryFindGamePakFile(value.Id);
                if (!string.IsNullOrEmpty(autoPak))
                {
                    PakFilePath = autoPak;
                    StatusMessage = $"'{value.ProcessName}' için doğrulama paketi otomatik seçildi: {Path.GetFileName(autoPak)}";
                }
            }
        }
    }

    [RelayCommand]
    public async Task StartStaticExeScanAsync()
    {
        var exePath = !string.IsNullOrWhiteSpace(ExeFilePath) ? ExeFilePath : SelectedProcess?.FilePath;

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            StatusMessage = "Lütfen önce taranacak oyunun .exe dosyasını seçin.";
            return;
        }

        if (string.IsNullOrWhiteSpace(PakFilePath) || !File.Exists(PakFilePath))
        {
            StatusMessage = "Lütfen doğrulamak için oyunun şifreli .pak dosyasını seçin.";
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        try
        {
            IsScanning = true;
            HasFoundKey = false;
            FoundAesKey = string.Empty;
            ScanProgress = 30;
            StatusMessage = $"'{Path.GetFileName(exePath)}' exe dosyası çalıştırılmadan statik taranıyor (yığın imm32 / imm64 sabitleri)...";

            var result = await Task.Run(() => StaticAesKeyScanner.ScanFile(exePath, PakFilePath, msg =>
            {
                StatusMessage = msg;
            }, ct), ct);

            if (result != null)
            {
                FoundAesKey = result.KeyHex;
                HasFoundKey = true;
                ScanProgress = 100;
                StatusMessage = $"🎉 AES-256 Anahtarı Statik Taramayla 0.4 sn'de Bulundu! ({result.Method}, Ofset: 0x{result.FileOffset:X})";
            }
            else
            {
                StatusMessage = "⚠️ Exe içinde statik anahtar bulunamadı. Oyunu çalıştırıp 'Belleği Tara' (RAM) butonunu deneyebilirsiniz.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Statik tarama iptal edildi.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Statik tarama hatası: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand]
    public async Task StartScanAsync()
    {
        if (SelectedProcess is null)
        {
            StatusMessage = "Lütfen önce listeden çalışan oyunu seçin.";
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        try
        {
            IsScanning = true;
            HasFoundKey = false;
            FoundAesKey = string.Empty;
            ScanProgress = 0;
            StatusMessage = $"'{SelectedProcess.ProcessName}' süreci taranıyor...";

            var progress = new Progress<(int Progress, string Status)>(update =>
            {
                ScanProgress = update.Progress;
                StatusMessage = update.Status;
            });

            var targetPak = !string.IsNullOrWhiteSpace(PakFilePath) && File.Exists(PakFilePath)
                ? PakFilePath
                : RamAesExtractor.TryFindGamePakFile(SelectedProcess.Id);

            if (!string.IsNullOrEmpty(targetPak) && string.IsNullOrEmpty(PakFilePath))
            {
                PakFilePath = targetPak;
            }

            var key = await RamAesExtractor.ScanProcessForAesKeyAsync(
                SelectedProcess.Id,
                targetPak,
                progress,
                ct);

            if (!string.IsNullOrEmpty(key))
            {
                FoundAesKey = key;
                HasFoundKey = true;
                StatusMessage = "🎉 AES-256 Anahtarı RAM'den Başarıyla Çıkarıldı!";
            }
            else
            {
                StatusMessage = string.IsNullOrEmpty(targetPak)
                    ? "⚠️ Doğrulama paketi (.pak) bulunamadı! Lütfen üstteki 'Gözat...' butonundan oyunun .pak dosyasını seçin."
                    : "⚠️ Bellekte eşleşen kesin bir AES anahtarı bulunamadı. Oyunun ana menüsüne kadar ilerlediğinden emin olun.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Tarama kullanıcı tarafından durduruldu.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand]
    public void CancelScan()
    {
        _scanCts?.Cancel();
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
