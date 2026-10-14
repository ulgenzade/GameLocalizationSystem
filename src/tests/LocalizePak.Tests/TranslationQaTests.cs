using LocalizePak.Core.Models;
using LocalizePak.Translation.Services;
using Xunit;

namespace LocalizePak.Tests;

public class TranslationQaTests
{
    [Fact]
    public void ValidateEntry_DetectsMqmCategoriesAndSeverities()
    {
        var entry = new LocalizationEntry
        {
            Key = "UI_START_BUTTON",
            SourceText = "Start Game", // 10 chars
            TranslatedText = "Maceraya ve Savaşa Şimdi Hemen Başla" // 35 chars (> 1.8x, overflow risk)
        };

        var issues = TranslationQaService.ValidateEntry(entry);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.IssueType == QaIssueType.OverflowRisk && 
                                     i.Category == MqmCategory.Design && 
                                     i.Severity == QaSeverity.Medium);
    }

    [Fact]
    public void ValidateEntry_DetectsUntranslatedContent()
    {
        var entry = new LocalizationEntry
        {
            Key = "DIALOGUE_LINE_1",
            SourceText = "Welcome to the magical kingdom of Eldoria",
            TranslatedText = "Welcome to the magical kingdom of Eldoria"
        };

        var issues = TranslationQaService.ValidateEntry(entry);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.IssueType == QaIssueType.UntranslatedContent && 
                                     i.Category == MqmCategory.Accuracy && 
                                     i.Severity == QaSeverity.High);
    }
}
