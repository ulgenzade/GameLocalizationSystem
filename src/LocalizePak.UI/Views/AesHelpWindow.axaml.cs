using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LocalizePak.UI.Views;

public partial class AesHelpWindow : Window
{
    public AesHelpWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
