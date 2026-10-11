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
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer?.Items.OfType<IStorageItem>().Select(i => i.TryGetLocalPath()).Where(p => p != null).ToList();
        if (files != null && files.Count > 0)
        {
            var firstFile = files.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstFile))
            {
                var ext = System.IO.Path.GetExtension(firstFile).ToLowerInvariant();
                if (ext is ".pak")
                {
                    await ViewModel.OpenPakFromPathAsync(firstFile);
                }
                else
                {
                    await ViewModel.OpenSingleFileFromPathAsync(firstFile);
                }
            }
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

    private async void OnRamAesExtractorClick(object? sender, RoutedEventArgs e)
    {
        var vm = new RamAesExtractorViewModel(ViewModel.LastOpenedPath);
        vm.OnKeyApplied = (key) =>
        {
            ViewModel.AesKey = key;
            ViewModel.StatusMessage = $"🔑 RAM'den alınan AES Anahtarı uygulandı: {key[..Math.Min(16, key.Length)]}...";
            if (!string.IsNullOrEmpty(ViewModel.LastOpenedPath))
            {
                _ = ViewModel.OpenPakFromPathAsync(ViewModel.LastOpenedPath);
            }
        };
        var win = new RamAesExtractorWindow { DataContext = vm };
        await win.ShowDialog(this);
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