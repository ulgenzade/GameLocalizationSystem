using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace LocalizePak.UI.Views;

public partial class RamAesExtractorWindow : Window
{
    public RamAesExtractorWindow()
    {
        InitializeComponent();
    }

    private async void OnBrowsePakClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.RamAesExtractorViewModel vm)
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Doğrulama Yapılacak .pak Arşivini Seçin",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new("Oyun Paketleri (*.pak)") { Patterns = ["*.pak"] },
                    new("Tüm Dosyalar (*.*)") { Patterns = ["*.*"] }
                ]
            };
            var result = await StorageProvider.OpenFilePickerAsync(options);
            if (result.Count > 0)
            {
                vm.PakFilePath = result[0].Path.LocalPath;
            }
        }
    }

    private async void OnBrowseExeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.RamAesExtractorViewModel vm)
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Taranacak Oyunun .exe Dosyasını Seçin",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new("Çalıştırılabilir Dosyalar (*.exe)") { Patterns = ["*.exe"] },
                    new("Tüm Dosyalar (*.*)") { Patterns = ["*.*"] }
                ]
            };
            var result = await StorageProvider.OpenFilePickerAsync(options);
            if (result.Count > 0)
            {
                vm.ExeFilePath = result[0].Path.LocalPath;
            }
        }
    }

    private void OnApplyCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.RamAesExtractorViewModel vm)
        {
            vm.ApplyKey();
        }
        Close(true);
    }
}
