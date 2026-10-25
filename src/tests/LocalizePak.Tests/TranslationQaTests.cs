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

    [Fact]
    public void GetHighestSeverity_WithMixedIssues_ReturnsCritical()
    {
        var entry = new LocalizationEntry
        {
            Key = "UI_GOLD",
            SourceText = "Found {0} gold",
            TranslatedText = "Bir miktar altın buldun, harika bir iş" // {0} eksik (Critical) + taşma (Medium)
        };

        var issues = TranslationQaService.ValidateEntry(entry);

        Assert.Contains(issues, i => i.Severity == QaSeverity.Medium);
        Assert.Equal(QaSeverity.Critical, TranslationQaService.GetHighestSeverity(issues));
    }

    [Fact]
    public void GetHighestSeverity_WithNoIssues_ReturnsNull()
    {
        Assert.Null(TranslationQaService.GetHighestSeverity([]));
    }

    [Fact]
    public void BuildIssueSummary_OrdersBySeverityAndUsesTurkishLabels()
    {
        var issues = new List<QaIssue>
        {
            new("K", "NS", "src", "tr", QaIssueType.OverflowRisk, "Taşma", false, MqmCategory.Design, QaSeverity.Medium),
            new("K", "NS", "src", "tr", QaIssueType.MissingVariable, "Değişken eksik", true, MqmCategory.Accuracy, QaSeverity.Critical)
        };

        var lines = TranslationQaService.BuildIssueSummary(issues).Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.Equal("🔴 Kritik · Doğruluk — Değişken eksik", lines[0]);
        Assert.Equal("🟡 Orta · Biçim/Arayüz — Taşma", lines[1]);
    }

    [Fact]
    public void BuildIssueSummary_WithNoIssues_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TranslationQaService.BuildIssueSummary([]));
    }
}
