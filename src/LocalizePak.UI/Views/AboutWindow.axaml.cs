using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LocalizePak.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnOpenGitHubClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com") { UseShellExecute = true });
        }
        catch { }
    }
}
