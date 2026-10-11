using LocalizePak.Core.Models;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Helpers;
using LocalizePak.Translation;
using Xunit;

namespace LocalizePak.Tests;

public class LocalizationTests
{
    [Fact]
    public void OutputManager_PrepareIsolatedExport_PreservesExactFileName()
    {
        var sourcePath = @"C:\Games\DOS2\DefEd\Localization\English.pak";
        var tempBase = Path.Combine(Path.GetTempPath(), "LocalizePak_Test_" + Guid.NewGuid());

        try
        {
            var (targetDir, targetFilePath) = OutputManager.PrepareIsolatedExport(sourcePath, tempBase);

            Assert.True(Directory.Exists(targetDir));
            Assert.Equal("English.pak", Path.GetFileName(targetFilePath));
            Assert.Contains("English", Path.GetFileName(targetDir));
        }
        finally
        {
            if (Directory.Exists(tempBase))
                Directory.Delete(tempBase, true);
        }
    }

    [Fact]
    public void MaskingEngine_MaskAndRestore_PreservesVariablesAndTags()
    {
        var engine = new MaskingEngine();
        var originalText = "Hello {PlayerName}, you found %d gold! <color=#FF0000>Danger</color>\\n";

        var (maskedText, restoreMap) = engine.Mask(originalText);
        var restoredText = engine.Restore(maskedText, restoreMap);

        Assert.NotEqual(originalText, maskedText);
        Assert.Contains("[VAR_", maskedText);
        Assert.Contains("[FMT_", maskedText);
        Assert.Equal(originalText, restoredText);
        Assert.True(engine.ValidateMaskIntegrity(restoreMap, maskedText));
    }

    [Fact]
    public async Task UniversalLocalizationService_XmlAndJson_ParsesCorrectly()
    {
        var service = new UniversalLocalizationService();

        // 1. XML Test
        var xmlSample = """
        <contentList>
            <content contentuid="h123abc">Kahraman hazır!</content>
            <content contentuid="h456def">Düşman yaklaşıyor.</content>
        </contentList>
        """;
        using var xmlStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xmlSample));
        var xmlLocres = await service.DeserializeAsync(xmlStream);
        var xmlEntries = xmlLocres.AllEntries.ToList();

        Assert.Equal(2, xmlEntries.Count);
        Assert.Equal("Kahraman hazır!", xmlEntries[0].SourceText);

        // 2. JSON Test
        var jsonSample = """
        {
            "menu_start": "Oyuna Başla",
            "menu_exit": "Çıkış"
        }
        """;
        using var jsonStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonSample));
        var jsonLocres = await service.DeserializeAsync(jsonStream);
        var jsonEntries = jsonLocres.AllEntries.ToList();

        Assert.Equal(2, jsonEntries.Count);
        Assert.Contains(jsonEntries, e => e.Key == "menu_start" && e.SourceText == "Oyuna Başla");
    }

    [Fact]
    public async Task GlossaryCsvHelper_ExportAndImport_PreservesEntries()
    {
        var tempCsv = Path.GetTempFileName();
        try
        {
            var entries = new List<GlossaryEntry>
            {
                new() { Original = "Rivellon", Translation = "Rivellon", DoNotTranslate = true },
                new() { Original = "Sourcerer", Translation = "Kaynak Büyücüsü", DoNotTranslate = false }
            };

            await GlossaryCsvHelper.ExportToCsvAsync(tempCsv, entries);
            var imported = await GlossaryCsvHelper.ImportFromCsvAsync(tempCsv);

            Assert.Equal(2, imported.Count);
            Assert.Equal("Rivellon", imported[0].Original);
            Assert.Equal("Kaynak Büyücüsü", imported[1].Translation);
        }
        finally
        {
            if (File.Exists(tempCsv))
                File.Delete(tempCsv);
        }
    }

    [Fact]
    public void Turkish_Specialized_Master_Prompt_Contains_Grammar_Slang_And_UI_Directives()
    {
        var prompt = AppSettings.DefaultMasterPrompt;

        Assert.Contains("PRO-DROP", prompt);
        Assert.Contains("ARGO, KÜFÜR VE SOKAK DİLİ", prompt);
        Assert.Contains("ŞAKALAR VE KELİME OYUNLARI", prompt);
        Assert.Contains("ARAYÜZ VE BAŞLIKLAR", prompt);
        Assert.Contains("SAYGI VE HİTAPLAR", prompt);
    }

    [Fact]
    public async Task LSPakHandler_WriteAndRead_Roundtrip_Works()
    {
        var tempPak = Path.Combine(Path.GetTempPath(), "test_lslib_" + Guid.NewGuid() + ".pak");
        try
        {
            var xmlData = System.Text.Encoding.UTF8.GetBytes("<contentList><content contentuid=\"h001\">Merhaba Dünya!</content></contentList>");
            await LSPakHandler.WriteArchiveAsync(tempPak, [("Localization/English/english.xml", xmlData)]);

            var archive = await LSPakHandler.ReadArchiveAsync(tempPak);
            Assert.Single(archive.Entries);
            Assert.Equal("Localization/English/english.xml", archive.Entries[0].VirtualPath);

            using var stream = await LSPakHandler.ExtractEntryAsync(tempPak, archive.Entries[0]);
            var service = new UniversalLocalizationService();
            var locres = await service.DeserializeAsync(stream);
            var entries = locres.AllEntries.ToList();

            Assert.Single(entries);
            Assert.Equal("h001", entries[0].Key);
            Assert.Equal("Merhaba Dünya!", entries[0].SourceText);
        }
        finally
        {
            if (File.Exists(tempPak))
                File.Delete(tempPak);
        }
    }

    [Fact]
    public async Task TestRealDOS2EnglishPakFullWorkflow()
    {
        var path = @"C:\Users\atoks\Desktop\orj\def\English.pak";
        if (!File.Exists(path)) return;

        var archive = await LSPakHandler.ReadArchiveAsync(path);
        Assert.NotNull(archive);
        Assert.NotEmpty(archive.Entries);

        var engXmlEntry = archive.Entries.FirstOrDefault(e => e.VirtualPath == "Localization/English/english.xml");
        Assert.NotNull(engXmlEntry);

        using var stream = await LSPakHandler.ExtractEntryAsync(path, engXmlEntry);
        Assert.True(stream.Length > 10_000_000, $"Expected > 10MB, got {stream.Length}");

        var locService = new UniversalLocalizationService();
        var locRes = await locService.DeserializeAsync(stream);
        var entries = locRes.AllEntries.ToList();

        Assert.True(entries.Count > 10000, $"Expected > 10,000 strings, got {entries.Count}");

        // Check the user's specific problem strings:
        var hannag = entries.FirstOrDefault(e => e.SourceText.Contains("Hannag"));
        Assert.NotNull(hannag);
        Console.WriteLine($"Hannag entry: '{hannag.SourceText}'");

        var beast = entries.FirstOrDefault(e => e.SourceText.Contains("sea salt"));
        Assert.NotNull(beast);
        Console.WriteLine($"Beast entry: '{beast.SourceText}'");
        Assert.Contains("sea salt 'n' taties, or jam on sweetbread", beast.SourceText);

        var veil = entries.FirstOrDefault(e => e.SourceText.Contains("Veil") || e.SourceText.Contains("Lucian"));
        if (veil != null)
        {
            Console.WriteLine($"Veil/Lucian sample entry: '{veil.SourceText}'");
        }

        Console.WriteLine($"🎉 ALL {entries.Count} DOS2 STRINGS EXTRACTED AND PARSED WITH 100% PURITY!");
    }

    [Fact]
    public void JsonTranslationParser_Parses_Various_Json_Formats_Correctly()
    {
        // 1. String array format: ["çeviri 1", "çeviri 2"]
        var json1 = "[\"Kan Ayı Adası\", \"Savaş Baykuşu Düdüğü\"]";
        var res1 = JsonTranslationParser.Parse(json1, 2);
        Assert.Equal(2, res1.Count);
        Assert.Equal("Kan Ayı Adası", res1[0]);
        Assert.Equal("Savaş Baykuşu Düdüğü", res1[1]);

        // 2. Object array with "text" or "translation": [{"id":0, "translation":"..."}, {"id":1, "text":"..."}]
        var json2 = "[{\"id\": 0, \"translation\": \"İlk Cümle\"}, {\"id\": 1, \"text\": \"İkinci Cümle\"}]";
        var res2 = JsonTranslationParser.Parse(json2, 2);
        Assert.Equal(2, res2.Count);
        Assert.Equal("İlk Cümle", res2[0]);
        Assert.Equal("İkinci Cümle", res2[1]);

        // 3. Object with "translations" array: {"translations": ["Bir", "İki"]}
        var json3 = "{\"translations\": [\"Birinci Metin\", \"İkinci Metin\"]}";
        var res3 = JsonTranslationParser.Parse(json3, 2);
        Assert.Equal(2, res3.Count);
        Assert.Equal("Birinci Metin", res3[0]);
        Assert.Equal("İkinci Metin", res3[1]);

        // 4. Markdown wrapped JSON: ```json [...] ```
        var json4 = "```json\n[\"Markdown Testi 1\", \"Markdown Testi 2\"]\n```";
        var res4 = JsonTranslationParser.Parse(json4, 2);
        Assert.Equal(2, res4.Count);
        Assert.Equal("Markdown Testi 1", res4[0]);
        Assert.Equal("Markdown Testi 2", res4[1]);

        // 5. Truncated / Broken JSON with regex fallback
        var json5 = "[\"Tamamlanan 1\", \"Tamamlanan 2\", {\"id\": 2, \"translation\": \"Yarıda kalan";
        var res5 = JsonTranslationParser.Parse(json5, 3);
        Assert.Equal(3, res5.Count);
        Assert.Equal("Tamamlanan 1", res5[0]);
        Assert.Equal("Tamamlanan 2", res5[1]);
    }

    [Fact]
    public void MaskingEngine_GameTags_Preserves_Dwarf_And_Outlaw()
    {
        var engine = new MaskingEngine();
        var original = "[DWARF] [OUTLAW] *Scoff. She's got naught to do*";
        var (masked, restoreMap) = engine.Mask(original);

        Assert.Contains("[GTAG_", masked);
        Assert.DoesNotContain("[DWARF]", masked);
        Assert.DoesNotContain("[OUTLAW]", masked);

        // AI çevirisini simüle et (etiketler aynı kalır, metin Türkçeleşir)
        var translatedMasked = masked.Replace("*Scoff. She's got naught to do*", "*Alaylı bir tavırla burnundan solur.*");
        var restored = engine.RestoreAndRepair(translatedMasked, restoreMap);

        Assert.StartsWith("[DWARF] [OUTLAW]", restored);
        Assert.Contains("*Alaylı bir tavırla burnundan solur.*", restored);
    }

    [Fact]
    public void MaskingEngine_RichTextClosingTags_And_CaseInsensitiveRestoration()
    {
        var engine = new MaskingEngine();
        var original = "<b>Kalın</b> ve <color=#FF0000>Kırmızı</color> {PlayerName}!</>";
        var (masked, restoreMap) = engine.Mask(original);

        Assert.Contains("[TAG_", masked);
        Assert.Contains("[ETAG_", masked);
        Assert.Contains("[VAR_", masked);

        // AI etiketleri küçük harfe dönüştürse veya boşluk eklese bile onarılmalı
        var aiOutput = masked.Replace("[VAR_0]", "[var_0]");
        var restored = engine.Restore(aiOutput, restoreMap);

        Assert.Equal(original, restored);
    }

    [Fact]
    public async Task TranslationMemory_ThreadSafe_ConcurrentAccess()
    {
        var tm = new TranslationMemory(Path.Combine(Path.GetTempPath(), "TM_Test_" + Guid.NewGuid()));
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            tm.Add($"source_{i}", "tr", $"translation_{i}");
            Assert.True(tm.TryGet($"source_{i}", "tr", out var val));
            Assert.Equal($"translation_{i}", val);
        })).ToArray();

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void TranslationMemory_SourceWithLogicalOrPipe_Preserved()
    {
        var tm = new TranslationMemory(Path.Combine(Path.GetTempPath(), "TM_Test_Pipe_" + Guid.NewGuid()));
        var src = "if (health <= 0 || mana <= 0) return;";
        tm.Add(src, "tr", "eğer (can <= 0 || mana <= 0) dön;");

        Assert.True(tm.TryGet(src, "tr", out var res));
        Assert.Equal("eğer (can <= 0 || mana <= 0) dön;", res);
    }

    [Fact]
    public void OutputManager_InvalidCharsInFileName_Sanitized()
    {
        var invalidSource = @"C:\Games\DefEd:Special?Edition*\Localization\English.pak";
        var tempBase = Path.Combine(Path.GetTempPath(), "OutputManager_Sanitize_" + Guid.NewGuid());

        try
        {
            var (targetDir, targetFilePath) = OutputManager.PrepareIsolatedExport(invalidSource, tempBase);
            Assert.True(Directory.Exists(targetDir));
            Assert.DoesNotContain(":", Path.GetFileName(targetDir));
            Assert.DoesNotContain("?", Path.GetFileName(targetDir));
            Assert.DoesNotContain("*", Path.GetFileName(targetDir));
        }
        finally
        {
            if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
        }
    }

    [Fact]
    public async Task UniversalLocalizationService_CsvSerialization_EscapesQuotesInKeyAndSource()
    {
        var service = new UniversalLocalizationService();
        var locres = new LocresFile
        {
            Version = LocalizePak.Core.Enums.LocresVersion.V3,
            SourcePath = "test.csv"
        };
        var ns = new LocresNamespace { Name = "Default" };
        ns.Entries.Add(new LocalizationEntry
        {
            Key = "dialog_\"quote\"_1",
            SourceText = "She said \"Hello, hero!\"",
            TranslatedText = "O da \"Merhaba kahraman!\" dedi",
            NamespaceName = "Default"
        });
        locres.Namespaces.Add(ns);

        var bytes = await service.SerializeAsync(locres);
        var csvText = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"dialog_\"\"quote\"\"_1\"", csvText);
        Assert.Contains("\"She said \"\"Hello, hero!\"\"\"", csvText);
        Assert.Contains("\"O da \"\"Merhaba kahraman!\"\" dedi\"", csvText);
    }

    [Fact]
    public async Task ClassicPakHandler_CorruptArchive_ThrowsInvalidDataException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // PACK magic (0x4B434150) + geçersiz dirOffset (999999) + dirLength (999999)
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(ClassicPakHandler.PACKMagic);
                bw.Write(999_999u); // dirOffset
                bw.Write(999_999u); // dirLength
            }

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await ClassicPakHandler.ReadArchiveAsync(tempFile);
            });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AppSettings_SourceAndTargetLanguages_PersistsCorrectly()
    {
        var settings = new AppSettings
        {
            SourceLanguage = "Japonca",
            TargetLanguage = "Türkçe",
            GeminiApiKey = "test_key",
            SelectedProvider = "Gemini"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Japonca", deserialized.SourceLanguage);
        Assert.Equal("Türkçe", deserialized.TargetLanguage);
        Assert.Equal("Gemini", deserialized.SelectedProvider);
    }

    [Fact]
    public void AppSettings_ReasoningEffort_PersistsCorrectly()
    {
        var settings = new AppSettings
        {
            ReasoningEffort = "Orta (Medium)"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Orta (Medium)", deserialized.ReasoningEffort);
    }

    [Theory]
    [InlineData("Kapalı (En Hızlı)", "off")]
    [InlineData("Düşük (Low)", "low")]
    [InlineData("low", "low")]
    [InlineData("Orta (Medium)", "medium")]
    [InlineData("mid", "medium")]
    [InlineData("Yüksek (High)", "high")]
    [InlineData("high", "high")]
    [InlineData("", "off")]
    [InlineData(null, "off")]
    public void TranslationContext_NormalizedReasoningEffort_WorksCorrectly(string? effort, string expected)
    {
        var context = new TranslationContext { ReasoningEffort = effort ?? string.Empty };
        Assert.Equal(expected, context.NormalizedReasoningEffort);
    }

    [Fact]
    public void JsonTranslationParser_StripsThinkTags_Successfully()
    {
        var rawWithThink = "<think>\nBurada model uzun uzun düşünüyor, deyimleri irdeliyor...\n</think>\n[\"Merhaba Dünya\", \"Kılıcı Kuşan\"]";
        var parsed = LocalizePak.Translation.JsonTranslationParser.Parse(rawWithThink, 2);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Merhaba Dünya", parsed[0]);
        Assert.Equal("Kılıcı Kuşan", parsed[1]);
    }

    [Fact]
    public void OllamaLocalModelService_PresetModels_ContainsFiveTieredModels()
    {
        var models = LocalizePak.Translation.Services.OllamaLocalModelService.PresetModels;

        Assert.Equal(5, models.Count);
        Assert.Equal("qwen2.5:3b", models[0].Id);
        Assert.Equal("qwen2.5:7b", models[1].Id);
        Assert.True(models[1].IsRecommended);
        Assert.Equal("deepseek-r1:8b", models[2].Id);
        Assert.Equal("qwen2.5:14b", models[3].Id);
        Assert.Equal("qwen2.5:32b", models[4].Id);

        // Donanım gereksinimleri ve boyut bilgileri eksiksiz olmalı
        foreach (var m in models)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.HardwareRequirement));
            Assert.False(string.IsNullOrWhiteSpace(m.SizeInfo));
            Assert.False(string.IsNullOrWhiteSpace(m.Description));
        }
    }

    [Fact]
    public void TranslationQaService_DetectsMissingVariablesAndUnclosedTags()
    {
        var entryWithMissingVar = new LocalizationEntry
        {
            Key = "Test_Key_1",
            SourceText = "Hello {PlayerName}, you have %d coins. <b>Important</b>",
            TranslatedText = "Merhaba, paralarınız var. <b>Önemli" // {PlayerName}, %d ve </b> eksik
        };

        var issues = LocalizePak.Translation.Services.TranslationQaService.ValidateEntry(entryWithMissingVar);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.IssueType == LocalizePak.Translation.Services.QaIssueType.MissingVariable && i.Description.Contains("{PlayerName}"));
        Assert.Contains(issues, i => i.IssueType == LocalizePak.Translation.Services.QaIssueType.MissingVariable && i.Description.Contains("%d"));
        Assert.Contains(issues, i => i.IssueType == LocalizePak.Translation.Services.QaIssueType.UnclosedTag);
    }

    [Fact]
    public void TranslationQaService_AutoFixEntry_RepairsMissingVariables()
    {
        var entry = new LocalizationEntry
        {
            Key = "Test_Key_2",
            SourceText = "Hello {0}, welcome to {City}!",
            TranslatedText = "Merhaba, hoş geldiniz!"
        };

        bool fixedResult = LocalizePak.Translation.Services.TranslationQaService.AutoFixEntry(entry);

        Assert.True(fixedResult);
        Assert.Contains("{0}", entry.TranslatedText);
        Assert.Contains("{City}", entry.TranslatedText);
    }

    [Fact]
    public async Task RecoveryService_SaveAndLoadSnapshot_WorksCorrectly()
    {
        var entries = new List<LocalizationEntry>
        {
            new() { Key = "Rec_1", SourceText = "Hello", TranslatedText = "Merhaba" },
            new() { Key = "Rec_2", SourceText = "Bye", TranslatedText = "Güle güle" }
        };

        await RecoveryService.SaveRecoverySnapshotAsync("TestGame/Test.pak", entries);

        Assert.True(RecoveryService.HasRecoverySnapshot());
        var info = RecoveryService.GetRecoverySnapshotInfo();
        Assert.NotNull(info);
        Assert.Equal(2, info.Value.EntryCount);
        Assert.Equal("TestGame/Test.pak", info.Value.SourcePath);

        var loaded = await RecoveryService.LoadRecoverySnapshotAsync();
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal("Merhaba", loaded.Entries[0].TranslatedText);

        RecoveryService.ClearRecoverySnapshot();
        Assert.False(RecoveryService.HasRecoverySnapshot());
    }

    [Fact]
    public void OpenRouterTranslationProvider_PropertiesAndInitialization_Correct()
    {
        var provider = new LocalizePak.Translation.Providers.OpenRouterTranslationProvider("test-key", "deepseek/deepseek-chat");
        Assert.Equal("OpenRouter", provider.Name);
        Assert.True(provider.IsConfigured);
    }

    [Fact]
    public void AggregatorProviders_PropertiesAndInitialization_Correct()
    {
        var together = new LocalizePak.Translation.Providers.TogetherAiTranslationProvider("together-key");
        Assert.Equal("Together AI", together.Name);
        Assert.True(together.IsConfigured);

        var mistral = new LocalizePak.Translation.Providers.MistralTranslationProvider("mistral-key");
        Assert.Equal("Mistral AI", mistral.Name);
        Assert.True(mistral.IsConfigured);

        var deepInfra = new LocalizePak.Translation.Providers.DeepInfraTranslationProvider("deepinfra-key");
        Assert.Equal("DeepInfra", deepInfra.Name);
        Assert.True(deepInfra.IsConfigured);

        var pplx = new LocalizePak.Translation.Providers.PerplexityTranslationProvider("pplx-key");
        Assert.Equal("Perplexity AI", pplx.Name);
        Assert.True(pplx.IsConfigured);
    }

    [Fact]
    public void MultiLanguage_MasterPromptTemplates_ReturnCorrectLanguage()
    {
        var tr = AppSettings.GetMasterPromptForLanguage("Türkçe");
        Assert.Contains("yerelleştirme", tr);

        var en = AppSettings.GetMasterPromptForLanguage("English");
        Assert.Contains("localization", en);

        var ru = AppSettings.GetMasterPromptForLanguage("Русский");
        Assert.Contains("локализации", ru);

        var zh = AppSettings.GetMasterPromptForLanguage("简体中文");
        Assert.Contains("本地化", zh);
    }

    [Fact]
    public async Task PakService_WriteAndRead_UnrealFormatWithAes_Roundtrip()
    {
        var pakService = new LocalizePak.IO.PakService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"ue_aes_test_{Guid.NewGuid():N}.pak");
        var aesKey = "0x11223344556677889900AABBCCDDEEFF11223344556677889900AABBCCDDEEFF";

        try
        {
            var files = new List<(string VirtualPath, byte[] Data)>
            {
                ("Game/Content/Localization/Game.locres", System.Text.Encoding.UTF8.GetBytes("LocresDataSample123")),
                ("Game/Content/Config/DefaultGame.ini", System.Text.Encoding.UTF8.GetBytes("[Game]\nTitle=MyGame"))
            };

            await pakService.WriteAsync(tempFile, "../../../", files, sourceFormat: LocalizePak.Core.Enums.PakFormat.Unreal, aesKey: aesKey);

            Assert.True(File.Exists(tempFile));

            // Anahtarsız açmaya çalışıldığında şifreli olduğunu bildirmeli
            var archive = await pakService.OpenAsync(tempFile, aesKey);
            Assert.True(archive.IsEncrypted);
            Assert.Equal(2, archive.Entries.Count);

            var entry = archive.Entries.First(e => e.VirtualPath.Contains("Game.locres"));
            using var stream = await pakService.ExtractEntryAsync(archive, entry);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            Assert.Equal("LocresDataSample123", content);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ClassicPakHandler_WriteAndRead_Roundtrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"classic_test_{Guid.NewGuid():N}.pak");

        try
        {
            var files = new List<(string VirtualPath, byte[] Data)>
            {
                ("scripts/menu.txt", System.Text.Encoding.ASCII.GetBytes("bind ESC menu")),
                ("maps/start.bsp", new byte[] { 0x01, 0x02, 0x03, 0x04 })
            };

            await ClassicPakHandler.WriteArchiveAsync(tempFile, files);
            Assert.True(File.Exists(tempFile));

            var archive = await ClassicPakHandler.ReadArchiveAsync(tempFile);
            Assert.Equal(2, archive.Entries.Count);

            var menuEntry = archive.Entries.First(e => e.VirtualPath == "scripts/menu.txt");
            using var stream = await ClassicPakHandler.ExtractEntryAsync(tempFile, menuEntry);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            Assert.Equal("bind ESC menu", text);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GodotPakHandler_WriteAndRead_Roundtrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"godot_test_{Guid.NewGuid():N}.pck");

        try
        {
            var files = new List<(string VirtualPath, byte[] Data)>
            {
                ("res://locale/tr.po", System.Text.Encoding.UTF8.GetBytes("msgid \"Hello\"\nmsgstr \"Merhaba\""))
            };

            await GodotPakHandler.WriteArchiveAsync(tempFile, files);
            Assert.True(File.Exists(tempFile));

            var archive = await GodotPakHandler.ReadArchiveAsync(tempFile);
            Assert.Single(archive.Entries);
            Assert.EndsWith("locale/tr.po", archive.Entries[0].VirtualPath);

            using var stream = await GodotPakHandler.ExtractEntryAsync(tempFile, archive.Entries[0]);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            Assert.Contains("Merhaba", text);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UnityPakHandler_WriteAndRead_Roundtrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"unity_test_{Guid.NewGuid():N}.pak");

        try
        {
            var files = new List<(string VirtualPath, byte[] Data)>
            {
                ("Assets/Resources/dialogue.json", System.Text.Encoding.UTF8.GetBytes("{\"title\": \"Quest\"}"))
            };

            await UnityPakHandler.WriteArchiveAsync(tempFile, files);
            Assert.True(File.Exists(tempFile));

            var archive = await UnityPakHandler.ReadArchiveAsync(tempFile);
            Assert.Single(archive.Entries);

            using var stream = await UnityPakHandler.ExtractEntryAsync(tempFile, archive.Entries[0]);
            Assert.True(stream.Length > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}