using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LocalizePak.Core.Models;
using LocalizePak.Translation.Services;
using LocalizePak.UI.ViewModels;

namespace LocalizePak.UI.Views;

public partial class AiAesAssistantWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AiGameDetectorService _detector = new();
    private GameEngineAndAesResult? _lastResult;

    public AiAesAssistantWindow() : this(new MainViewModel())
    {
    }

    public AiAesAssistantWindow(MainViewModel vm, string? initialGameGuess = null)
    {
        InitializeComponent();
        _vm = vm;

        if (!string.IsNullOrWhiteSpace(initialGameGuess))
        {
            GameNameTextBox.Text = initialGameGuess;
            AdvGameNameTextBox.Text = initialGameGuess;
        }

        ProviderComboBox.ItemsSource = MainViewModel.ProviderNames;
        ProviderComboBox.SelectedItem = _vm.SelectedProviderName;
        ProviderComboBox.SelectionChanged += (s, e) =>
        {
            if (ProviderComboBox.SelectedItem is string prov)
            {
                _vm.SelectedProviderName = prov;
            }
        };
    }

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (EasyModeCard == null || AdvancedModeCard == null) return;

        bool isEasy = EasyModeRadio.IsChecked == true;
        EasyModeCard.IsVisible = isEasy;
        AdvancedModeCard.IsVisible = !isEasy;
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        bool isEasy = EasyModeRadio.IsChecked == true;

        string gameName = isEasy
            ? GameNameTextBox.Text?.Trim() ?? string.Empty
            : AdvGameNameTextBox.Text?.Trim() ?? string.Empty;

        string dev = isEasy
            ? DeveloperTextBox.Text?.Trim() ?? string.Empty
            : AdvDeveloperTextBox.Text?.Trim() ?? string.Empty;

        string year = isEasy
            ? YearTextBox.Text?.Trim() ?? string.Empty
            : string.Empty;

        string? hint = !isEasy
            ? EngineHintTextBox.Text?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(gameName))
        {
            if (isEasy) GameNameTextBox.Focus();
            else AdvGameNameTextBox.Focus();
            return;
        }

        try
        {
            SearchButton.IsEnabled = false;
            LoadingBar.IsVisible = true;
            ResultCard.IsVisible = false;

            var provider = _vm.CreateProvider();
            _lastResult = await _detector.DetectEngineAndAesKeyAsync(gameName, dev, year, hint, provider);

            // Sonuçları doldur
            EngineText.Text = string.IsNullOrWhiteSpace(_lastResult.EngineVersion)
                ? _lastResult.EngineName
                : $"{_lastResult.EngineName} ({_lastResult.EngineVersion})";

            AdviceText.Text = _lastResult.SearchAdvice;

            if (_lastResult.HasAesKey)
            {
                FoundKeyPanel.IsVisible = true;
                FoundKeyTextBox.Text = _lastResult.FoundAesKey;
            }
            else
            {
                FoundKeyPanel.IsVisible = false;
            }

            ResultCard.IsVisible = true;
        }
        catch (Exception ex)
        {
            AdviceText.Text = $"Yapay zeka analiz hatası: {ex.Message}. Lütfen API ayarlarınızı kontrol edin.";
            ResultCard.IsVisible = true;
        }
        finally
        {
            SearchButton.IsEnabled = true;
            LoadingBar.IsVisible = false;
        }
    }

    private void OnApplyFoundKeyClick(object? sender, RoutedEventArgs e)
    {
        if (_lastResult?.FoundAesKey != null)
        {
            ApplyKeyAndClose(_lastResult.FoundAesKey);
        }
    }

    private void OnApplyManualKeyClick(object? sender, RoutedEventArgs e)
    {
        var manual = ManualKeyTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            ApplyKeyAndClose(manual);
        }
    }

    private void ApplyKeyAndClose(string key)
    {
        _vm.AesKey = key;
        Close();

        // Eğer açılmış bir dosya varsa tekrar açmayı dene
        if (!string.IsNullOrEmpty(_vm.LastOpenedPath))
        {
            _ = _vm.OpenPakFromPathAsync(_vm.LastOpenedPath);
        }
    }

    private void OnOpenBrowserSearchClick(object? sender, RoutedEventArgs e)
    {
        if (_lastResult != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_lastResult.DirectSearchUrl) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
