using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LocalizePak.UI.Views;

public partial class AesKeyVaultWindow : Window
{
    public AesKeyVaultWindow()
    {
        InitializeComponent();
    }

    private async void OnCopyKeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string key && !string.IsNullOrWhiteSpace(key))
        {
            await ViewModels.MainViewModel.SetClipboardTextAsync(key);
            if (DataContext is ViewModels.AesKeyVaultViewModel vm)
            {
                vm.StatusMessage = "📋 Anahtar panoya kopyalandı: " + key;
            }
        }
    }

    private void OnApplyKeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string key && !string.IsNullOrWhiteSpace(key))
        {
            if (DataContext is ViewModels.AesKeyVaultViewModel vm)
            {
                vm.OnKeySelected?.Invoke(key);
            }
            Close(key);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
