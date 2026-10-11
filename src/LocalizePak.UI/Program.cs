using Avalonia;
using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace LocalizePak.UI;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Tüm beklenmedik hatalara global yakalayıcı ekle
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var msg = e.ExceptionObject?.ToString() ?? "Bilinmeyen hata";
            try { File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] UnhandledException:\n{msg}\n\n"); } catch { }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            var msg = e.Exception?.ToString() ?? "Bilinmeyen task hatası";
            try { File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] UnobservedTaskException:\n{msg}\n\n"); } catch { }
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
