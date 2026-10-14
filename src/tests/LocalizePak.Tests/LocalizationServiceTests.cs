using System.Linq;
using LocalizePak.UI.Localization;
using LocalizePak.UI.Localization.Languages;
using Xunit;

namespace LocalizePak.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void AllFourLanguages_MustContainAllCoreKeys()
    {
        var trKeys = StringsTr.Map.Keys.OrderBy(k => k).ToList();
        var enKeys = StringsEn.Map.Keys.ToHashSet();
        var ruKeys = StringsRu.Map.Keys.ToHashSet();
        var zhKeys = StringsZh.Map.Keys.ToHashSet();

        Assert.NotEmpty(trKeys);

        foreach (var key in trKeys)
        {
            Assert.True(enKeys.Contains(key), $"English dictionary missing key: '{key}'");
            Assert.True(ruKeys.Contains(key), $"Russian dictionary missing key: '{key}'");
            Assert.True(zhKeys.Contains(key), $"Chinese dictionary missing key: '{key}'");
        }
    }

    [Theory]
    [InlineData("Türkçe", "tr", "GLS — Game Localization System")]
    [InlineData("English", "en", "GLS — Game Localization System")]
    [InlineData("Русский", "ru", "GLS — Game Localization System")]
    [InlineData("简体中文", "zh", "GLS — 游戏本地化系统")]
    public void SwitchLanguage_MustReturnCorrectAppTitle(string langInput, string expectedCode, string expectedTitle)
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage(langInput);

        Assert.Equal(expectedCode, loc.CurrentLanguage);
        Assert.Equal(expectedTitle, loc["App_Title"]);
    }

    [Fact]
    public void SwitchLanguage_MustReturnDistinctFileMenuAcrossLanguages()
    {
        var loc = LocalizationService.Instance;

        loc.SetLanguage("Türkçe");
        Assert.Equal("_Dosya", loc["Menu_File"]);

        loc.SetLanguage("English");
        Assert.Equal("_File", loc["Menu_File"]);

        loc.SetLanguage("Русский");
        Assert.Equal("_Файл", loc["Menu_File"]);

        loc.SetLanguage("简体中文");
        Assert.Equal("文件(_F)", loc["Menu_File"]);
    }

    [Fact]
    public void Format_MustFormatArgumentsAcrossLanguages()
    {
        var loc = LocalizationService.Instance;

        loc.SetLanguage("Türkçe");
        Assert.Equal("Dosya yüklendi: test.pak (42 satır)", loc.Format("Status_FileLoaded", "test.pak", 42));

        loc.SetLanguage("English");
        Assert.Equal("File loaded: test.pak (42 lines)", loc.Format("Status_FileLoaded", "test.pak", 42));

        loc.SetLanguage("Русский");
        Assert.Equal("Файл загружен: test.pak (42 строк)", loc.Format("Status_FileLoaded", "test.pak", 42));

        loc.SetLanguage("简体中文");
        Assert.Equal("已加载文件: test.pak (42 行)", loc.Format("Status_FileLoaded", "test.pak", 42));
    }
}
