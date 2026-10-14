using System;
using System.IO;
using System.Text;

namespace LocalizePak.IO.Services;

/// <summary>
/// 5846 Sayılı FSEK (Fikir ve Sanat Eserleri Kanunu) uyumlu, yalnızca delta yama verisini
/// (*_P.pak vb.) dağıtan, otomatik yedekleme ve geri alma (uninstall) mekanizmalı
/// tek tıkla bağımsız yama kurulum sihirbazı üreteci.
/// </summary>
public static class PatchInstallerGenerator
{
    public const string FsekDisclaimer =
        "Bu Türkçe yama paketi, 5846 sayılı Fikir ve Sanat Eserleri Kanunu (FSEK) hükümleri " +
        "çerçevesinde yalnızca dil yerelleştirmesi amacıyla hazırlanmıştır. " +
        "Orijinal oyunun telif hakkına tabi ikili kodlarını içermez. " +
        "Yalnızca orijinal oyuna sahip kullanıcıların kendi kopyalarına uygulanabilir ve ticari amaçla satılamaz.";

    /// <summary>
    /// Bir yama klasörü için otomatik yedekleme ve tek tıkla kurulum batch betiği üretir.
    /// </summary>
    public static string GenerateInstallerBatch(
        string gameName,
        string patchFileName,
        string targetSubfolder = @"Content\Paks\~mods")
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine("title " + gameName + " — Türkçe Yama Kurulum Sihirbazı");
        sb.AppendLine("cls");
        sb.AppendLine("echo ================================================================");
        sb.AppendLine("echo   " + gameName + " — Türkçe Yama Kurulum Aracı");
        sb.AppendLine("echo ================================================================");
        sb.AppendLine("echo.");
        sb.AppendLine("echo YASAL BİLGİLENDİRME (5846 FSEK):");
        sb.AppendLine("echo " + FsekDisclaimer);
        sb.AppendLine("echo.");
        sb.AppendLine("echo Kurulumu başlatmak için oyunun ana klasörünü belirtin");
        sb.AppendLine("echo (veya bu dosyayı oyun klasörüne kopyalayıp ENTER tuşuna basın).");
        sb.AppendLine("echo.");
        sb.AppendLine("set /p GAMEDIR=\"Oyun Klasörü [Varsayılan: Geçerli Dizin]: \"");
        sb.AppendLine("if \"%GAMEDIR%\"==\"\" set GAMEDIR=%cd%");
        sb.AppendLine();
        sb.AppendLine("set TARGETDIR=%GAMEDIR%\\" + targetSubfolder);
        sb.AppendLine("if not exist \"%TARGETDIR%\" mkdir \"%TARGETDIR%\"");
        sb.AppendLine();
        sb.AppendLine("echo [1/3] Mevcut durum denetleniyor...");
        sb.AppendLine("if exist \"%TARGETDIR%\\" + patchFileName + "\" (");
        sb.AppendLine("    echo [2/3] Eski yama yedeği alınıyor: " + patchFileName + ".bak");
        sb.AppendLine("    copy /y \"%TARGETDIR%\\" + patchFileName + "\" \"%TARGETDIR%\\" + patchFileName + ".bak\" >nul");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("echo [3/3] Türkçe yama kopyalanıyor: " + patchFileName);
        sb.AppendLine("copy /y \"" + patchFileName + "\" \"%TARGETDIR%\\" + patchFileName + "\" >nul");
        sb.AppendLine();
        sb.AppendLine("if %errorlevel% equ 0 (");
        sb.AppendLine("    echo.");
        sb.AppendLine("    echo ================================================================");
        sb.AppendLine("    echo   TEBRİKLER! Türkçe yama başarıyla kuruldu.");
        sb.AppendLine("    echo   İyi oyunlar!");
        sb.AppendLine("    echo ================================================================");
        sb.AppendLine(") else (");
        sb.AppendLine("    echo.");
        sb.AppendLine("    echo HATA: Dosya kopyalanamadı! Lütfen yönetici olarak çalıştırın.");
        sb.AppendLine(")");
        sb.AppendLine("pause");

        return sb.ToString();
    }

    /// <summary>
    /// Yamayı tamamen kaldırıp orijinal durumuna döndüren tek tıkla kaldırma betiği üretir.
    /// </summary>
    public static string GenerateUninstallerBatch(
        string gameName,
        string patchFileName,
        string targetSubfolder = @"Content\Paks\~mods")
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine("title " + gameName + " — Türkçe Yama Kaldırıcı");
        sb.AppendLine("cls");
        sb.AppendLine("echo ================================================================");
        sb.AppendLine("echo   " + gameName + " — Türkçe Yama Kaldırma ve Orijinale Dönüş");
        sb.AppendLine("echo ================================================================");
        sb.AppendLine("echo.");
        sb.AppendLine("set /p GAMEDIR=\"Oyun Klasörü [Varsayılan: Geçerli Dizin]: \"");
        sb.AppendLine("if \"%GAMEDIR%\"==\"\" set GAMEDIR=%cd%");
        sb.AppendLine();
        sb.AppendLine("set TARGETFILE=%GAMEDIR%\\" + targetSubfolder + "\\" + patchFileName);
        sb.AppendLine("if exist \"%TARGETFILE%\" (");
        sb.AppendLine("    del /f /q \"%TARGETFILE%\"");
        sb.AppendLine("    echo Türkçe yama dosyası silindi.");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("if exist \"%TARGETFILE%.bak\" (");
        sb.AppendLine("    move /y \"%TARGETFILE%.bak\" \"%TARGETFILE%\" >nul");
        sb.AppendLine("    echo Orijinal yedeğiniz başarıyla geri yüklendi.");
        sb.AppendLine(")");
        sb.AppendLine("echo Orijinal oyun dosyalarına dönüldü.");
        sb.AppendLine("pause");

        return sb.ToString();
    }

    /// <summary>
    /// Dağıtıma hazır bir yama paketi klasörü (Patch, Installer, Uninstaller, Readme) oluşturur.
    /// </summary>
    public static void CreateDistributionPackage(
        string outputDirectory,
        string gameName,
        string patchFileName,
        byte[] patchData,
        string targetSubfolder = @"Content\Paks\~mods")
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // 1. Yama dosyası
        File.WriteAllBytes(Path.Combine(outputDirectory, patchFileName), patchData);

        // 2. Kurulum betiği
        string installBat = GenerateInstallerBatch(gameName, patchFileName, targetSubfolder);
        File.WriteAllText(Path.Combine(outputDirectory, "Kurulum_ve_Yedekle.bat"), installBat, Encoding.UTF8);

        // 3. Kaldırma betiği
        string uninstallBat = GenerateUninstallerBatch(gameName, patchFileName, targetSubfolder);
        File.WriteAllText(Path.Combine(outputDirectory, "Yamayi_Kaldir_Orijinale_Don.bat"), uninstallBat, Encoding.UTF8);

        // 4. Beni Oku & Lisans / 5846 FSEK Bildirisi
        var readme = new StringBuilder();
        readme.AppendLine($"# {gameName} Türkçe Yama Paketi");
        readme.AppendLine();
        readme.AppendLine("## Kurulum Yöntemi");
        readme.AppendLine("1. Bu klasörün içindeki tüm dosyaları oyunun kurulu olduğu ana klasöre kopyalayın.");
        readme.AppendLine("2. `Kurulum_ve_Yedekle.bat` dosyasına çift tıklayarak çalıştırın.");
        readme.AppendLine("3. Kurulum otomatik olarak tamamlanacak ve orijinal dosyalarınız korunacaktır.");
        readme.AppendLine();
        readme.AppendLine("## Yamayı Kaldırma");
        readme.AppendLine("Yamayı kaldırmak ve oyunu orijinal diline döndürmek için `Yamayi_Kaldir_Orijinale_Don.bat` dosyasına çift tıklamanız yeterlidir.");
        readme.AppendLine();
        readme.AppendLine("## Hukuki Uyarı (5846 FSEK)");
        readme.AppendLine(FsekDisclaimer);

        File.WriteAllText(Path.Combine(outputDirectory, "BENI_OKU_VE_LISANS.txt"), readme.ToString(), Encoding.UTF8);
    }
}
