using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LocalizePak.Core.Models;
using LocalizePak.IO.Helpers;
using LocalizePak.UI.ViewModels;

namespace LocalizePak.UI.Views;

public partial class GlossaryWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    public GlossaryWindow()
    {
        InitializeComponent();
    }

    private void OnAddEntryClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GlossaryList.Add(new GlossaryEntry
        {
            Original = "Yeni Terim",
            DoNotTranslate = true
        });
    }

    private void OnDeleteEntryClick(object? sender, RoutedEventArgs e)
    {
        if (GlossaryGrid.SelectedItem is GlossaryEntry selected)
        {
            ViewModel.GlossaryList.Remove(selected);
        }
    }

    private async void OnImportCsvClick(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Glosary CSV Dosyası Yükle",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV Dosyası") { Patterns = ["*.csv"] }]
        };

        // async void olay işleyicisinde yakalanmayan hata tüm uygulamayı kapatır; bozuk/kilitli CSV'ye karşı koru.
        try
        {
            var result = await StorageProvider.OpenFilePickerAsync(options);
            var path = result.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            var entries = await GlossaryCsvHelper.ImportFromCsvAsync(path);
            var existing = new HashSet<string>(ViewModel.GlossaryList.Select(g => g.Original), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var item in entries)
            {
                if (existing.Add(item.Original))
                {
                    ViewModel.GlossaryList.Add(item);
                    added++;
                }
            }
            ViewModel.StatusMessage = $"📥 Sözlüğe {added} yeni terim eklendi.";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"⚠️ CSV içe aktarılamadı: {ex.Message}";
        }
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerSaveOptions
        {
            Title = "Glosary CSV Olarak Kaydet",
            SuggestedFileName = "Glossary_Export.csv",
            FileTypeChoices = [new FilePickerFileType("CSV Dosyası") { Patterns = ["*.csv"] }]
        };

        try
        {
            var result = await StorageProvider.SaveFilePickerAsync(options);
            var path = result?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                await GlossaryCsvHelper.ExportToCsvAsync(path, ViewModel.GlossaryList);
                ViewModel.StatusMessage = $"📤 Sözlük dışa aktarıldı: {Path.GetFileName(path)}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"⚠️ CSV dışa aktarılamadı: {ex.Message}";
        }
    }

    private async void OnSaveAndCloseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"⚠️ Ayarlar kaydedilemedi: {ex.Message}";
        }
        Close();
    }
}
