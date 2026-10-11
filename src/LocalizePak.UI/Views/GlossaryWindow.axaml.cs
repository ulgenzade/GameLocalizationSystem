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

        var result = await StorageProvider.OpenFilePickerAsync(options);
        var path = result.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            var entries = await GlossaryCsvHelper.ImportFromCsvAsync(path);
            foreach (var item in entries)
            {
                if (!ViewModel.GlossaryList.Any(g => g.Original.Equals(item.Original, StringComparison.OrdinalIgnoreCase)))
                {
                    ViewModel.GlossaryList.Add(item);
                }
            }
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

        var result = await StorageProvider.SaveFilePickerAsync(options);
        var path = result?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            await GlossaryCsvHelper.ExportToCsvAsync(path, ViewModel.GlossaryList);
        }
    }

    private async void OnSaveAndCloseClick(object? sender, RoutedEventArgs e)
    {
        await ViewModel.SaveSettingsAsync();
        Close();
    }
}
