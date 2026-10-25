using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LocalizePak.UI.ViewModels;

namespace LocalizePak.UI.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (e.DataTransfer == null) return;

            string? localPath = null;

            // 1. Dosya listesi (Avalonia DataTransferExtensions.TryGetFiles)
            var files = e.DataTransfer.TryGetFiles();
            if (files != null && files.Length > 0)
            {
                localPath = files[0].TryGetLocalPath();
            }

            // 2. Tekil dosya (TryGetFile)
            if (string.IsNullOrEmpty(localPath))
            {
                var file = e.DataTransfer.TryGetFile();
                localPath = file?.TryGetLocalPath();
            }

            // 3. Metin/Yol yedeği
            if (string.IsNullOrEmpty(localPath))
            {
                var text = e.DataTransfer.TryGetText();
                if (!string.IsNullOrWhiteSpace(text) && System.IO.File.Exists(text.Trim()))
                {
                    localPath = text.Trim();
                }
            }

            if (!string.IsNullOrEmpty(localPath) && System.IO.File.Exists(localPath))
            {
                var ext = System.IO.Path.GetExtension(localPath).ToLowerInvariant();
                if (MainViewModel.IsArchivePath(localPath))
                {
                    await ViewModel.OpenPakFromPathAsync(localPath);
                }
                else
                {
                    await ViewModel.OpenSingleFileFromPathAsync(localPath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DragDrop hatası: {ex}");
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Dosya dialog köprüleri — string[][] (her eleman [name, ...patterns])
        ViewModel.ShowOpenFileDialogFunc = async (title, filterArrays) =>
        {
            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filterArrays.Select(f =>
                    new FilePickerFileType(f.Length > 0 ? f[0] : "Dosya")
                    {
                        Patterns = f.Skip(1).ToList()
                    }).ToList()
            };
            var result = await StorageProvider.OpenFilePickerAsync(options);
            return result.FirstOrDefault()?.TryGetLocalPath();
        };

        ViewModel.ShowSaveFileDialogFunc = async (title, filterArrays, suggested) =>
        {
            var options = new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggested,
                FileTypeChoices = filterArrays.Select(f =>
                    new FilePickerFileType(f.Length > 0 ? f[0] : "Dosya")
                    {
                        Patterns = f.Skip(1).ToList()
                    }).ToList()
            };
            var result = await StorageProvider.SaveFilePickerAsync(options);
            return result?.TryGetLocalPath();
        };
    }

    private void OnDataGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        OpenDiffInspector();
    }

    private void OnInspectDiffClick(object? sender, RoutedEventArgs e)
    {
        OpenDiffInspector();
    }

    private void OpenDiffInspector()
    {
        if (EntriesDataGrid.SelectedItem is LocalizationEntryViewModel selected)
        {
            var inspector = new DiffInspectorWindow { DataContext = selected };
            inspector.ShowDialog(this);
        }
        else
        {
            StatusBar_ShowMessage("İncelemek için tablodan bir satır seçin.");
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow { DataContext = ViewModel };
        settings.ShowDialog(this);
    }

    private void OnGlossaryClick(object? sender, RoutedEventArgs e)
    {
        var glossaryWin = new GlossaryWindow { DataContext = ViewModel };
        glossaryWin.ShowDialog(this);
    }

    private void OnAiAesAssistantClick(object? sender, RoutedEventArgs e)
    {
        var guess = !string.IsNullOrEmpty(ViewModel.LastOpenedPath)
            ? System.IO.Path.GetFileNameWithoutExtension(ViewModel.LastOpenedPath)
            : null;

        var assistant = new AiAesAssistantWindow(ViewModel, guess);
        assistant.ShowDialog(this);
    }

    private void OnAesHelpClick(object? sender, RoutedEventArgs e)
    {
        var aesWin = new AesHelpWindow();
        aesWin.ShowDialog(this);
    }

    private async void OnAesKeyVaultClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Anahtar hem OnKeySelected geri çağrısıyla hem de diyalog sonucu olarak dönebilir;
            // aynı anahtar için paketi iki kez (eşzamanlı) açmamak adına uygulananı hatırla.
            string? appliedKey = null;
            void ApplyKey(string key)
            {
                if (key == appliedKey) return;
                appliedKey = key;
                ViewModel.AesKey = key;
                ViewModel.StatusMessage = $"🔑 Anahtar Havuzundan uygulandı: {key[..Math.Min(16, key.Length)]}...";
                if (!string.IsNullOrEmpty(ViewModel.LastOpenedPath))
                {
                    _ = ViewModel.OpenPakFromPathAsync(ViewModel.LastOpenedPath);
                }
            }

            var vm = new AesKeyVaultViewModel { OnKeySelected = ApplyKey };
            var win = new AesKeyVaultWindow { DataContext = vm };
            var result = await win.ShowDialog<string?>(this);
            if (!string.IsNullOrWhiteSpace(result))
                ApplyKey(result);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"⚠️ Anahtar Havuzu açılamadı: {ex.Message}";
        }
    }

    private async void OnRamAesExtractorClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = new RamAesExtractorViewModel(ViewModel.LastOpenedPath);
            vm.OnKeyApplied = (key) =>
            {
                ViewModel.AesKey = key;
                ViewModel.StatusMessage = $"🔑 RAM'den alınan AES Anahtarı uygulandı: {key[..Math.Min(16, key.Length)]}...";
                var targetPath = !string.IsNullOrEmpty(ViewModel.LastOpenedPath)
                    ? ViewModel.LastOpenedPath
                    : vm.PakFilePath;

                if (!string.IsNullOrEmpty(targetPath))
                {
                    _ = ViewModel.OpenPakFromPathAsync(targetPath);
                }
            };
            var win = new RamAesExtractorWindow { DataContext = vm };
            await win.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"⚠️ RAM AES aracı açılamadı: {ex.Message}";
        }
    }

    private void OnHelpClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // WORKFLOW.md veya docs klasörünü ara
            var candidates = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "docs", "WORKFLOW.md"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "WORKFLOW.md"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "WORKFLOW.md")
            };

            var target = candidates.FirstOrDefault(System.IO.File.Exists);
            if (target != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetFullPath(target)) { UseShellExecute = true });
                StatusBar_ShowMessage("📖 Kullanım kılavuzu açıldı.");
                return;
            }
        }
        catch { }

        StatusBar_ShowMessage("Yardım için docs/WORKFLOW.md dosyasına bakın.");
    }

    private void OnLocalizationGuideClick(object? sender, RoutedEventArgs e)
    {
        var guide = new LocalizationGuideWindow();
        guide.ShowDialog(this);
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var aboutWin = new AboutWindow();
        aboutWin.ShowDialog(this);
    }

    private void StatusBar_ShowMessage(string msg)
    {
        if (DataContext is MainViewModel vm)
            vm.StatusMessage = msg;
    }
}