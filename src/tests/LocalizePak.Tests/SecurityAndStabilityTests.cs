using System.IO.Compression;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Helpers;
using LocalizePak.IO.Services;
using LocalizePak.Translation;
using LocalizePak.UI.ViewModels;
using Xunit;

namespace LocalizePak.Tests;

/// <summary>
/// Güvenlik denetimi düzeltmeleri: zip-slip, sıkıştırma bombası, girdi sınırları, .bat enjeksiyonu,
/// API anahtarı şifreleme ve kararlılık hataları.
/// </summary>
public class SecurityAndStabilityTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gls-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ─── Zip-slip ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("Content/../../evil.txt")]
    [InlineData("..\\..\\evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("C:evil.dll")]
    public void ResolveChildPath_RejectsPathsEscapingRoot(string virtualPath)
    {
        var root = NewTempDir();
        Assert.Throws<InvalidDataException>(() => SafeIo.ResolveChildPath(root, virtualPath));
    }

    [Theory]
    [InlineData("Content/Localization/Game/en/Game.locres")]
    [InlineData("/Content/a.txt")]
    [InlineData("Content\\b.txt")]
    [InlineData("Content/./sub/../c.txt")]
    public void ResolveChildPath_AllowsNormalArchivePaths(string virtualPath)
    {
        var root = NewTempDir();
        var full = SafeIo.ResolveChildPath(root, virtualPath);
        Assert.StartsWith(Path.GetFullPath(root), full, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Boyut sınırları ve sıkıştırma bombası ──────────────────────────────

    [Fact]
    public void CopyBounded_ThrowsWhenLimitExceeded()
    {
        using var src = new MemoryStream(new byte[10_000]);
        using var dst = new MemoryStream();
        Assert.Throws<DecompressionLimitExceededException>(() => SafeIo.CopyBounded(src, dst, 5_000));
    }

    [Fact]
    public void CopyBounded_CopiesWithinLimit()
    {
        using var src = new MemoryStream(new byte[10_000]);
        using var dst = new MemoryStream();
        SafeIo.CopyBounded(src, dst, 10_000);
        Assert.Equal(10_000, dst.Length);
    }

    [Theory]
    [InlineData(0, SafeIo.MaxEntrySize)]
    [InlineData(1000, 1000 + 1024 * 1024)]
    [InlineData(100L * 1024 * 1024, 110L * 1024 * 1024)]
    [InlineData(2L * 1024 * 1024 * 1024, SafeIo.MaxEntrySize)]
    public void DecompressionLimit_AddsMarginButNeverExceedsMax(long declared, long expected)
    {
        Assert.Equal(expected, SafeIo.DecompressionLimit(declared));
    }

    [Fact]
    public void ValidateEntryRange_RejectsHugeOrOutOfFileEntries()
    {
        var huge = new PakEntry { VirtualPath = "a", DataOffset = 0, UncompressedSize = 5L * 1024 * 1024 * 1024 };
        var outOfFile = new PakEntry { VirtualPath = "b", DataOffset = 900, UncompressedSize = 200 };
        var negative = new PakEntry { VirtualPath = "c", DataOffset = -1, UncompressedSize = 10 };

        Assert.Throws<InvalidDataException>(() => SafeIo.ValidateEntryRange(huge, 1000, checkUncompressedOnly: true));
        Assert.Throws<InvalidDataException>(() => SafeIo.ValidateEntryRange(outOfFile, 1000, checkUncompressedOnly: true));
        Assert.Throws<InvalidDataException>(() => SafeIo.ValidateEntryRange(negative, 1000, checkUncompressedOnly: true));
    }

    [Fact]
    public async Task GodotExtract_CorruptEntrySize_ThrowsInsteadOfAllocating()
    {
        var file = Path.Combine(NewTempDir(), "test.pck");
        await File.WriteAllBytesAsync(file, new byte[1024]);
        var entry = new PakEntry { VirtualPath = "x", DataOffset = 0, UncompressedSize = int.MaxValue };

        await Assert.ThrowsAsync<InvalidDataException>(() => GodotPakHandler.ExtractEntryAsync(file, entry));
    }

    [Fact]
    public async Task ZipExtract_NormalEntry_StillWorks()
    {
        var file = Path.Combine(NewTempDir(), "test.pak");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("Localization/en.json");
            await using var s = e.Open();
            await s.WriteAsync(new byte[50_000]);
        }

        await using var result = await ZipPakHandler.ExtractEntryAsync(file, new PakEntry { VirtualPath = "Localization/en.json" });
        Assert.Equal(50_000, result.Length);
    }

    // ─── .bat komut enjeksiyonu ──────────────────────────────────────────────

    [Fact]
    public void InstallerBatch_StripsCommandInjectionCharacters()
    {
        var bat = PatchInstallerGenerator.GenerateInstallerBatch(
            "Tom & Jerry\r\ndel /q C:\\*", "..\\..\\pakchunk0_P.pak%PATH%");

        Assert.DoesNotContain("& Jerry", bat);
        Assert.DoesNotContain("\r\ndel /q", bat);
        Assert.DoesNotContain("%PATH%", bat);
        Assert.DoesNotContain("..\\..\\", bat);
        Assert.Contains("pakchunk0_P.pakPATH", bat);
    }

    [Fact]
    public void SanitizeForBatch_KeepsNormalNamesAndPaths()
    {
        Assert.Equal("Hogwarts Legacy", SafeIo.SanitizeForBatch("Hogwarts Legacy"));
        Assert.Equal(@"Content\Paks\~mods", SafeIo.SanitizeForBatch(@"Content\Paks\~mods"));
    }

    // ─── API anahtarı şifreleme ──────────────────────────────────────────────

    [Fact]
    public void Settings_ApiKeysAreEncryptedOnDiskAndRoundTrip()
    {
        var settings = new AppSettings { GeminiApiKey = "AIza-test-123", ClaudeApiKey = "sk-ant-test-456", GeminiModel = "gemini-3.5-flash-lite" };

        var json = SettingsService.SerializeAndProtect(settings);
        var back = SettingsService.DeserializeAndUnprotect(json);

        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain("AIza-test-123", json);
            Assert.DoesNotContain("sk-ant-test-456", json);
            Assert.Contains(SettingsService.ProtectedPrefix, json);
        }
        Assert.Contains("gemini-3.5-flash-lite", json); // Anahtar olmayan alanlar düz kalır
        Assert.Equal("AIza-test-123", back.GeminiApiKey);
        Assert.Equal("sk-ant-test-456", back.ClaudeApiKey);
        Assert.Equal("gemini-3.5-flash-lite", back.GeminiModel);
    }

    [Fact]
    public void Settings_LegacyPlaintextKeysAreStillReadable()
    {
        var back = SettingsService.DeserializeAndUnprotect("""{"GeminiApiKey":"legacy-plain","GeminiModel":"gemini-3.8-flash"}""");

        Assert.Equal("legacy-plain", back.GeminiApiKey);
        Assert.Equal("gemini-3.8-flash", back.GeminiModel);
    }

    // ─── Kararlılık ──────────────────────────────────────────────────────────

    private sealed class EmptyResultProvider : ITranslationProvider
    {
        public string Name => "Boş";
        public bool IsConfigured => true;
        public Task<IReadOnlyList<string>> TranslateBatchAsync(IReadOnlyList<string> maskedTexts, TranslationContext context, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(maskedTexts.Select(_ => string.Empty).ToList());
    }

    [Fact]
    public async Task Orchestrator_EmptyAiResult_KeepsPreviousManualTranslation()
    {
        var entry = new LocalizationEntry
        {
            Key = "K1",
            SourceText = "Welcome to the kingdom",
            TranslatedText = "Krallığa hoş geldin",
            Status = TranslationStatus.ManuallyEdited
        };

        await new BatchTranslationOrchestrator().TranslateEntriesAsync([entry], new EmptyResultProvider(), new TranslationContext(), batchSize: 5, maxConcurrency: 1);

        Assert.Equal(TranslationStatus.Error, entry.Status);
        Assert.Equal("Krallığa hoş geldin", entry.TranslatedText);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("Hello", 1)]
    [InlineData("  Found   500 gold  ", 3)]
    [InlineData("The ancient gate is waiting", 5)]
    public void CountWords_MatchesSplitBehaviour(string text, int expected)
    {
        Assert.Equal(expected, MainViewModel.CountWords(text));
        Assert.Equal(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, MainViewModel.CountWords(text));
    }
}
