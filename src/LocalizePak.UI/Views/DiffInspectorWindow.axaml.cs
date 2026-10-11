using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LocalizePak.UI.Views;

public partial class DiffInspectorWindow : Window
{
    public DiffInspectorWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
