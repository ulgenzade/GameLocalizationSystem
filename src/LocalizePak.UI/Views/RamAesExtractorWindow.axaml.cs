using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LocalizePak.UI.Views;

public partial class RamAesExtractorWindow : Window
{
    public RamAesExtractorWindow()
    {
        InitializeComponent();
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
