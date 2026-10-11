using System.Diagnostics;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Çıktı klasörlerini tarihe göre düzenleyen, orijinal dosya adını koruyan ve
/// birden fazla çevirinin birbirine karışmasını engelleyen yardımcı servis.
/// </summary>
public static class OutputManager
{
    private static readonly string DefaultExportsBaseDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Exports");

    /// <summary>
    /// Verilen .pak kaynak dosyası için izole, tarihli ve karışıklığı önleyen bir çıktı yolu üretir.
    /// Örnek: Exports/2026-08-19_13-45-22_DefEd_English/English.pak
    /// </summary>
    /// <param name="sourcePakPath">Orijinal yüklenen .pak dosyasının tam yolu.</param>
    /// <param name="baseDirectory">Özel çıktı ana dizini (boş ise uygulama yanındaki Exports/ kullanılır).</param>
    /// <returns>Oluşturulan klasörün yolu ve tam hedef dosya yolu.</returns>
    public static (string TargetDirectory, string TargetFilePath) PrepareIsolatedExport(
        string sourcePakPath,
        string? baseDirectory = null)
    {
        var rootDir = string.IsNullOrWhiteSpace(baseDirectory)
            ? DefaultExportsBaseDir
            : baseDirectory;

        // Orijinal dosya adı (Örn: English.pak)
        var fileName = Path.GetFileName(sourcePakPath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePakPath);

        // Üst klasör adı (Örn: "DefEd" veya "Data" gibi ayrım yapabilmek için)
        var parentDirName = Path.GetFileName(Path.GetDirectoryName(sourcePakPath));
        var contextPrefix = !string.IsNullOrEmpty(parentDirName) && !parentDirName.Equals("Localization", StringComparison.OrdinalIgnoreCase)
            ? $"{SanitizeFileName(parentDirName)}_"
            : string.Empty;

        // Tarih damgası: YYYY-MM-DD_HH-mm-ss
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Klasör Adı: 2026-08-19_13-45-22_DefEd_English (Geçersiz karakterlerden arındırılmış)
        var safeFileNameWithoutExt = SanitizeFileName(fileNameWithoutExt);
        var folderName = $"{timestamp}_{contextPrefix}{safeFileNameWithoutExt}";
        var targetDir = Path.Combine(rootDir, folderName);

        // Klasörü oluştur
        Directory.CreateDirectory(targetDir);

        // Çıktı dosya adı: Orijinal dosya adıyla BİREBİR AYNI (English.pak)
        var targetFilePath = Path.Combine(targetDir, fileName);

        return (targetDir, targetFilePath);
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "Output";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Oluşturulan çıktı klasörünü Windows Dosya Gezgini'nde açar.
    /// </summary>
    /// <param name="folderPath">Açılacak klasör yolu.</param>
    public static bool OpenFolderInExplorer(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true,
                Verb = "open"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
