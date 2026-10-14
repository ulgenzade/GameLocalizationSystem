using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.Core.Models;
using LocalizePak.IO.Services;

namespace LocalizePak.UI.ViewModels;

public partial class AesKeyVaultViewModel : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Bilinen oyunların AES-256 anahtar havuzu hazır.";

    [ObservableProperty]
    private bool _isUpdatingCloud;

    [ObservableProperty]
    private AesKeyVaultEntry? _selectedEntry;

    // Yeni Anahtar Ekleme Formu
    [ObservableProperty]
    private bool _isAddDialogVisible;

    [ObservableProperty]
    private string _newGameName = string.Empty;

    [ObservableProperty]
    private string _newAesKey = string.Empty;

    [ObservableProperty]
    private string _newEngineVersion = "UE4 / UE5";

    [ObservableProperty]
    private string _newPakPattern = string.Empty;

    public ObservableCollection<AesKeyVaultEntry> FilteredKeys { get; } = new();

    public Action<string>? OnKeySelected { get; set; }

    public AesKeyVaultViewModel()
    {
        RefreshList();
    }

    public void RefreshList()
    {
        FilteredKeys.Clear();
        var results = string.IsNullOrWhiteSpace(SearchText)
            ? AesKeyVaultService.GetAllKeys()
            : AesKeyVaultService.Search(SearchText);

        foreach (var r in results)
        {
            FilteredKeys.Add(r);
        }

        StatusMessage = $"{FilteredKeys.Count} adet oyun şifresi listelendi.";
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshList();
    }

    [RelayCommand]
    public void ApplySelectedKey(AesKeyVaultEntry? entry)
    {
        var target = entry ?? SelectedEntry;
        if (target != null && !string.IsNullOrWhiteSpace(target.AesKey))
        {
            OnKeySelected?.Invoke(target.AesKey);
            StatusMessage = $"'{target.GameName}' anahtarı uygulandı!";
        }
    }

    [RelayCommand]
    public async Task UpdateFromCloudAsync()
    {
        try
        {
            IsUpdatingCloud = true;
            StatusMessage = "Bulut depolarına bağlanılıyor...";

            var progress = new Progress<string>(msg => StatusMessage = msg);
            int added = await AesKeyVaultService.UpdateFromCloudAsync(progress);

            RefreshList();
            StatusMessage = added > 0
                ? $"🎉 Buluttan {added} yeni oyun şifresi havuza eklendi!"
                : "Havuz zaten en güncel sürümde.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Bulut senkronizasyonu hatası: {ex.Message}";
        }
        finally
        {
            IsUpdatingCloud = false;
        }
    }

    [RelayCommand]
    public void OpenAddDialog()
    {
        IsAddDialogVisible = true;
        NewGameName = string.Empty;
        NewAesKey = string.Empty;
        NewPakPattern = string.Empty;
    }

    [RelayCommand]
    public void CancelAdd()
    {
        IsAddDialogVisible = false;
    }

    [RelayCommand]
    public void SaveNewKey()
    {
        if (string.IsNullOrWhiteSpace(NewGameName) || string.IsNullOrWhiteSpace(NewAesKey))
        {
            StatusMessage = "Oyun adı ve AES anahtarı boş bırakılamaz.";
            return;
        }

        var entry = new AesKeyVaultEntry
        {
            GameName = NewGameName.Trim(),
            AesKey = NewAesKey.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? NewAesKey.Trim() : "0x" + NewAesKey.Trim(),
            EngineVersion = NewEngineVersion.Trim(),
            PakPattern = NewPakPattern.Trim(),
            IsCustom = true
        };

        AesKeyVaultService.AddOrUpdateKey(entry);
        IsAddDialogVisible = false;
        RefreshList();
        StatusMessage = $"'{entry.GameName}' anahtar havuzuna kaydedildi!";
    }
}
