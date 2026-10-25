using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO;
using LocalizePak.IO.Helpers;
using LocalizePak.IO.Services;

namespace LocalizePak.UI.ViewModels;

public sealed partial class MainViewModel
{
    // ─── Dışa Aktarma ve Yama Durumu ────────────────────────────────────────

    [ObservableProperty]
    private string? _lastExportedDirectory;

    [ObservableProperty]
    private bool _hasExportedDirectory;

    // ─── Otomatik Oturum Kurtarma (Disaster Recovery) ────────────────────────

    [ObservableProperty]
    private bool _hasRecoverySession;

    [ObservableProperty]
    private string _recoverySessionInfo = string.Empty;

    // ─── Dışa Aktarma Komutları ─────────────────────────────────────────────

    /// <summary>
    /// Yazılacak paketin AES anahtarı: yalnızca kaynak arşiv gerçekten şifreliyse. Şifresiz bir oyunun yaması şifrelenirse
    /// (ör. Anahtar Havuzu'ndan yanlış eşleşen bir anahtarla) motor onu açamaz ve yama hiç yüklenmez.
    /// </summary>
    private string? PatchAesKey() =>
        _currentArchive is { IsEncrypted: true }
            ? _currentArchive.AesKey ?? (string.IsNullOrWhiteSpace(AesKey) ? null : AesKey)
            : null;

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(LastExportedDirectory))
        {
            OutputManager.OpenFolderInExplorer(LastExportedDirectory);
        }
    }

    /// <summary>
    /// Çeviriyi, orijinal dosya adıyla BİREBİR AYNI isimde, tarihe göre adlandırılmış
    /// izole bir çıktı klasörüne otomatik olarak kaydeder (Karışmayı %100 önler).
    /// </summary>
    [RelayCommand]
    private async Task ExportToTimestampedFolderAsync()
    {
        if (_currentLocresFile is null || _currentArchive is null)
        {
            StatusMessage = "Önce bir .pak arşivi ve lokalizasyon dosyası açmalısınız.";
            return;
        }

        if (_currentArchive.IsIoStore)
        {
            StatusMessage = "IoStore (.utoc/.ucas) arşivleri doğrudan yeniden yazılamaz; 'Yama Paketi' ile _P yaması oluşturun.";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Tarihli çıktı klasörü hazırlanıyor...";

            var (targetDir, targetFilePath) = OutputManager.PrepareIsolatedExport(_currentArchive.FilePath);
            var mountPoint = _currentArchive.MountPoint;

            // Tüm orijinal arşiv varlıklarını (dokunulmaz font, ses, config dosyaları dahil) topla
            var filesToWrite = await PrepareAllArchiveFilesForExportAsync();

            // Birebir aynı dosya adıyla (örn. English.pak) tarihli klasöre yaz
            await _pakService.WriteAsync(targetFilePath, mountPoint, filesToWrite,
                sourceFormat: _currentArchive.Format,
                aesKey: PatchAesKey(),
                sourceArchivePath: _currentArchive.FilePath);

            // Bilgi notu dosyası oluştur
            var infoPath = Path.Combine(targetDir, "cevirme_bilgisi.txt");
            var infoContent = $"""
            ====================================================
            LOCALIZEPAK ÇEVİRİ VE YAMA BİLGİSİ
            ====================================================
            Tarih/Saat      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            Orijinal Dosya  : {_currentArchive.FilePath}
            Çıktı Dosyası   : {Path.GetFileName(targetFilePath)}
            Toplam Satır    : {TotalEntries}
            Tamamlanan      : {CompletedEntries}
            Hata Sayısı     : {ErrorCount}
            ====================================================
            KULLANIM:
            Bu klasördeki '{Path.GetFileName(targetFilePath)}' dosyasını oyunun
            ilgili klasörüne atıp üzerine yazarak yamayı aktifleştirebilirsiniz.
            """;
            await File.WriteAllTextAsync(infoPath, infoContent);

            LastExportedDirectory = targetDir;
            HasExportedDirectory = true;
            StatusMessage = $"✅ Orijinal isimle çıktı oluşturuldu: {Path.GetFileName(targetDir)}\\{Path.GetFileName(targetFilePath)}";

            OutputManager.OpenFolderInExplorer(targetDir);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dışa aktarma hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportTranslationsCsvAsync()
    {
        if (_currentLocresFile is null)
        {
            StatusMessage = "Dışa aktarılacak metin bulunamadı.";
            return;
        }

        var savePath = await ShowSaveFileDialogAsync(
            "Çeviri Tablosunu CSV Olarak Kaydet",
            [["*.csv"]],
            "Ceviriler_" + Path.GetFileNameWithoutExtension(_currentLocresFile.SourcePath ?? "Localization") + ".csv");

        if (savePath is null) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Namespace,Key,SourceText,TranslatedText");
            foreach (var item in LocalizationEntries)
            {
                var ns = item.Namespace.Replace("\"", "\"\"");
                var key = item.Key.Replace("\"", "\"\"");
                var src = item.SourceText.Replace("\"", "\"\"");
                var tr = item.TranslatedText.Replace("\"", "\"\"");
                sb.AppendLine($"\"{ns}\",\"{key}\",\"{src}\",\"{tr}\"");
            }
            await File.WriteAllTextAsync(savePath, sb.ToString(), System.Text.Encoding.UTF8);
            StatusMessage = $"CSV başarıyla kaydedildi: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV dışa aktarma hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportErrorsCsvAsync()
    {
        var errorEntries = LocalizationEntries.Where(e => e.Status == TranslationStatus.Error).ToList();
        if (errorEntries.Count == 0)
        {
            StatusMessage = "Dışa aktarılacak hatalı satır bulunamadı (Tüm satırlar başarılı!).";
            return;
        }

        var defaultName = $"Hata_Raporu_{DateTime.Now:yyyy-MM-dd_HH-mm}_{errorEntries.Count}_Hata.csv";
        var savePath = await ShowSaveFileDialogAsync(
            "Hatalı Satırları CSV Olarak Kaydet",
            [["*.csv"]],
            defaultName);

        if (savePath is null) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Durum,Namespace,Anahtar,Kaynak Metin (Orijinal),Hedef Çeviri,Karakter Sayısı,Olası Hata Nedeni");
            foreach (var item in errorEntries)
            {
                var ns = CsvSafe(item.Namespace);
                var key = CsvSafe(item.Key);
                var src = CsvSafe(item.SourceText);
                var tr = CsvSafe(item.TranslatedText);
                var charLen = item.SourceText.Length;
                var reason = string.IsNullOrWhiteSpace(item.SourceText) ? "Boş metin" :
                             (item.SourceText.Contains("<?xml") || item.SourceText.Contains("<save>") || item.SourceText.Contains("StartTime") || item.SourceText.Contains("build90")) ? "XML / Kod etiketi kırıntısı (Çevrilemez ikili kod)" :
                             "Yapay zeka güvenlik filtresi / Maskeleme uyuşmazlığı";
                sb.AppendLine($"\"Hata\",\"{ns}\",\"{key}\",\"{src}\",\"{tr}\",\"{charLen}\",\"{reason}\"");
            }

            await File.WriteAllTextAsync(savePath, sb.ToString(), System.Text.Encoding.UTF8);
            StatusMessage = $"⚠️ {errorEntries.Count} hatalı satır CSV raporu olarak kaydedildi: {Path.GetFileName(savePath)}";

            var folder = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(folder))
            {
                OutputManager.OpenFolderInExplorer(folder);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata raporu dışa aktarma hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportTranslationsCsvAsync()
    {
        if (_currentLocresFile is null)
        {
            StatusMessage = "Önce bir .pak veya lokalizasyon dosyası açmalısınız.";
            return;
        }

        var openPath = await ShowOpenFileDialogAsync(
            "Düzenlenmiş CSV Dosyasını Seç",
            [["*.csv"]]);

        if (openPath is null || !File.Exists(openPath)) return;

        try
        {
            var content = await File.ReadAllTextAsync(openPath, System.Text.Encoding.UTF8);
            var rows = ParseCsv(content);
            int importedCount = 0;

            var map = new Dictionary<string, string>();
            for (int i = 1; i < rows.Count; i++)
            {
                var parts = rows[i];
                if (parts.Length >= 4 && !string.IsNullOrEmpty(parts[1]))
                {
                    map[parts[1]] = parts[3]; // Key -> TranslatedText
                }
            }

            foreach (var entry in LocalizationEntries)
            {
                if (map.TryGetValue(entry.Key, out var newTrans) && !string.IsNullOrEmpty(newTrans))
                {
                    entry.TranslatedText = newTrans;
                    entry.RefreshFromModel();
                    importedCount++;
                }
            }

            ApplyFilter();
            CompletedEntries = LocalizationEntries.Count(e => e.Status == TranslationStatus.Done || e.Status == TranslationStatus.ManuallyEdited);
            StatusMessage = $"CSV'den {importedCount} satır çeviri başarıyla içe aktarıldı.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV içe aktarma hatası: {ex.Message}";
        }
    }

    private static List<string[]> ParseCsv(string content)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cur.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                fields.Add(cur.ToString()); cur.Clear();
                if (fields.Count > 1 || fields[0].Length > 0) rows.Add(fields.ToArray());
                fields.Clear();
            }
            else cur.Append(c);
        }

        if (cur.Length > 0 || fields.Count > 0)
        {
            fields.Add(cur.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }

    private static string CsvSafe(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
            v = "'" + v;
        return v.Replace("\"", "\"\"");
    }

    private async Task<List<(string VirtualPath, byte[] Data)>> PrepareAllArchiveFilesForExportAsync()
    {
        var filesToWrite = new List<(string VirtualPath, byte[] Data)>();
        if (_currentArchive is null) return filesToWrite;

        foreach (var entry in _currentArchive.Entries)
        {
            if (_archiveLocresCache.TryGetValue(entry.VirtualPath, out var cachedLocres))
            {
                var data = await _locresService.SerializeAsync(cachedLocres);
                filesToWrite.Add((entry.VirtualPath, data));
            }
            else if (_currentLocresFile is not null && string.Equals(_currentLocresFile.SourcePath, entry.VirtualPath, StringComparison.OrdinalIgnoreCase))
            {
                var data = await _locresService.SerializeAsync(_currentLocresFile);
                filesToWrite.Add((entry.VirtualPath, data));
            }
            else
            {
                try
                {
                    using var origStream = await _pakService.ExtractEntryAsync(_currentArchive, entry);
                    using var ms = new MemoryStream();
                    await origStream.CopyToAsync(ms);
                    filesToWrite.Add((entry.VirtualPath, ms.ToArray()));
                }
                catch
                {
                }
            }
        }

        return filesToWrite;
    }

    [RelayCommand]
    private async Task SavePakDirectAsync()
    {
        if (_currentLocresFile is null) return;

        if (_currentArchive is null)
        {
            var defaultSingleName = Path.GetFileName(_currentLocresFile.SourcePath ?? "Localization.locres");
            var outputSinglePath = await ShowSaveFileDialogAsync(
                "Dosyayı Kaydet",
                [["*.*"]],
                defaultSingleName);
            if (outputSinglePath is null) return;

            try
            {
                IsLoading = true;
                StatusMessage = "Dosya kaydediliyor...";
                var data = await _locresService.SerializeAsync(_currentLocresFile);
                await File.WriteAllBytesAsync(outputSinglePath, data);
                LastExportedDirectory = Path.GetDirectoryName(outputSinglePath);
                HasExportedDirectory = !string.IsNullOrEmpty(LastExportedDirectory);
                StatusMessage = $"Dosya kaydedildi: {Path.GetFileName(outputSinglePath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Kaydetme hatası: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
            return;
        }

        if (_currentArchive.IsIoStore)
        {
            // .ucas içindeki paketler eski .pak biçimine dökülürse oyun onları yüklemez; değişiklik yalnızca _P yamasıyla taşınır
            StatusMessage = "IoStore (.utoc/.ucas) arşivleri doğrudan yeniden yazılamaz; 'Yama Paketi' ile _P yaması oluşturun.";
            return;
        }

        var defaultName = Path.GetFileName(_currentArchive.FilePath);
        var outputPath = await ShowSaveFileDialogAsync(
            ".pak Dosyasını Kaydet (Doğrudan Değiştir)",
            [["*.pak"]],
            defaultName);

        if (outputPath is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Paket dosyası kaydediliyor...";

            var mountPoint = _currentArchive.MountPoint;
            var filesToWrite = await PrepareAllArchiveFilesForExportAsync();

            await _pakService.WriteAsync(outputPath, mountPoint, filesToWrite,
                sourceFormat: _currentArchive.Format,
                aesKey: PatchAesKey(),
                sourceArchivePath: _currentArchive.FilePath);

            LastExportedDirectory = Path.GetDirectoryName(outputPath);
            HasExportedDirectory = !string.IsNullOrEmpty(LastExportedDirectory);

            StatusMessage = $"Paket kaydedildi: {Path.GetFileName(outputPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kaydetme hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportPatchAsync()
    {
        if (_currentLocresFile is null || _currentArchive is null) return;

        var baseName = Path.GetFileNameWithoutExtension(_currentArchive.FileName);
        string suggestedPatch;
        if (_currentArchive.Format == PakFormat.Unreal)
        {
            suggestedPatch = baseName.EndsWith("_P", StringComparison.OrdinalIgnoreCase)
                ? baseName + "1.pak"
                : baseName + "_P.pak";
        }
        else
        {
            suggestedPatch = baseName + "-Patch.pak";
        }

        var outputPath = await ShowSaveFileDialogAsync(
            "Yama paketi kaydet (_P.pak)",
            [["*.pak"]],
            suggestedPatch);

        if (outputPath is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Yama paketi oluşturuluyor...";

            var filesToWrite = new List<(string VirtualPath, byte[] Data)>();

            if (_currentLocresFile.SourcePath == AllFilesVirtualPath)
            {
                foreach (var entry in _currentArchive.LocalizationEntries)
                {
                    if (_archiveLocresCache.TryGetValue(entry.VirtualPath, out var cachedLocres))
                    {
                        var data = await _locresService.SerializeAsync(cachedLocres);
                        filesToWrite.Add((entry.VirtualPath, data));
                    }
                }
            }
            else
            {
                var locresData = await _locresService.SerializeAsync(_currentLocresFile);
                var virtualPath = _currentLocresFile.SourcePath ?? _currentArchive.LocalizationEntries.FirstOrDefault()?.VirtualPath ?? "Game/Content/Localization/Game.locres";
                filesToWrite.Add((virtualPath, locresData));
            }

            if (filesToWrite.Count == 0)
            {
                StatusMessage = "Yama paketine eklenecek geçerli lokalizasyon dosyası bulunamadı.";
                return;
            }

            // Paketlerden üretilen Game.locres (oyunun kendi yerelleştirmesi yok): motor yalnızca etkin kültürün klasörünü
            // okur; "en" yanında hedef dil klasörüne de yazılır ki oyun hangi dilde açılırsa açılsın çeviri görünsün.
            var targetIso = MapLanguageToIsoCode(SelectedTargetLanguage);
            if (!string.IsNullOrEmpty(targetIso) && targetIso is not ("en" or "auto"))
            {
                foreach (var generated in _currentArchive.Entries.Where(e => e.IsGenerated))
                {
                    var match = filesToWrite.FirstOrDefault(f => string.Equals(f.VirtualPath, generated.VirtualPath, StringComparison.OrdinalIgnoreCase));
                    if (match.Data != null)
                        filesToWrite.Add((generated.VirtualPath.Replace("/en/", $"/{targetIso}/"), match.Data));
                }
            }

            var mountPoint = _currentArchive.MountPoint;
            await _pakService.WriteAsync(outputPath, mountPoint,
                filesToWrite,
                sourceFormat: _currentArchive.Format,
                aesKey: PatchAesKey(),
                sourceArchivePath: _currentArchive.FilePath);

            // IoStore oyunları: motorun .pak ile birlikte aradığı _P.utoc/_P.ucas eşi
            if (_currentArchive.IsIoStore)
                await PakService.WriteIoStoreCompanionAsync(outputPath, _currentArchive);

            LastExportedDirectory = Path.GetDirectoryName(outputPath);
            HasExportedDirectory = !string.IsNullOrEmpty(LastExportedDirectory);

            StatusMessage = _currentArchive.IsIoStore
                ? $"Yama oluşturuldu: {Path.GetFileName(outputPath)} + .utoc/.ucas (üçü birlikte Paks klasörüne kopyalanmalı)"
                : $"Yama paketi oluşturuldu: {Path.GetFileName(outputPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dışa aktarma hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task CheckRecoverySessionAsync()
    {
        var info = RecoveryService.GetRecoverySnapshotInfo();
        if (info != null)
        {
            HasRecoverySession = true;
            RecoverySessionInfo = $"⚠️ Kurtarılabilir Çeviri Oturumu Tespit Edildi ({Path.GetFileName(info.Value.SourcePath)} — {info.Value.EntryCount} satır, {info.Value.Timestamp:HH:mm:ss})";
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task RestoreRecoverySessionAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Önceki çeviri oturumu kurtarılıyor...";
            var snapshot = await RecoveryService.LoadRecoverySnapshotAsync();
            if (snapshot == null || snapshot.Entries.Count == 0)
            {
                HasRecoverySession = false;
                StatusMessage = "Kurtarılacak oturum verisi bulunamadı.";
                return;
            }

            var locres = new LocresFile
            {
                SourcePath = snapshot.SourcePath,
                Version = LocresVersion.V3
            };
            var ns = new LocresNamespace { Name = "Kurtarilan_Metinler" };
            foreach (var e in snapshot.Entries) ns.Entries.Add(e);
            locres.Namespaces.Add(ns);
            _currentLocresFile = locres;

            var vms = snapshot.Entries.Select(e => new LocalizationEntryViewModel(e)).ToList();
            LocalizationEntries = new ObservableCollection<LocalizationEntryViewModel>(vms);
            TotalEntries = vms.Count;
            CompletedEntries = vms.Count(e => e.Status == TranslationStatus.Done || e.Status == TranslationStatus.ManuallyEdited);
            ErrorCount = vms.Count(e => e.Status == TranslationStatus.Error);
            ApplyFilter();

            LoadedPakFileName = Path.GetFileName(snapshot.SourcePath);
            IsPakLoaded = true;
            HasRecoverySession = false;
            StatusMessage = $"✅ Çeviri oturumu başarıyla kurtarıldı! ({TotalEntries} satır, {CompletedEntries} tamamlanmış)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kurtarma hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void DismissRecoverySession()
    {
        RecoveryService.ClearRecoverySnapshot();
        HasRecoverySession = false;
        StatusMessage = "Kurtarma oturumu temizlendi.";
    }
}
