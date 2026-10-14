using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LocalizePak.UI.Views;

public partial class LocalizationGuideWindow : Window
{
    public LocalizationGuideWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
