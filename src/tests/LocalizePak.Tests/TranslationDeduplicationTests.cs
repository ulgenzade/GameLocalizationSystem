using System.Collections.Generic;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.Translation.Services;
using Xunit;

namespace LocalizePak.Tests;

public class TranslationDeduplicationTests
{
    private readonly TranslationDeduplicationEngine _engine = new();

    [Fact]
    public void ExtractTemplate_NumbersAreExtracted()
    {
        var (template, values) = _engine.ExtractTemplate("Found 100 gold and 5 gems");
        Assert.Equal("Found __NUM_0__ gold and __NUM_1__ gems", template);
        Assert.Equal(2, values.Count);
        Assert.Equal("100", values[0]);
        Assert.Equal("5", values[1]);
    }

    [Fact]
    public void AnalyzeAndCluster_GroupsSimilarTemplates()
    {
        var entries = new List<LocalizationEntry>
        {
            new() { SourceText = "Found 100 gold" },
            new() { SourceText = "Found 250 gold" },
            new() { SourceText = "Found 500 gold" },
            new() { SourceText = "Talk to NPC" }
        };

        var analysis = _engine.AnalyzeAndCluster(entries);

        Assert.Equal(4, analysis.TotalEntries);
        Assert.Equal(2, analysis.RepresentativeCount);
        Assert.Equal(2, analysis.RedundantEntriesCount);
        Assert.Equal(50.0, analysis.EstimatedTokenSavingsPercent);
    }

    [Fact]
    public void PropagateClusterTranslations_CorrectlyInterpolatesDynamicValues()
    {
        var entry1 = new LocalizationEntry { SourceText = "Found 100 gold" };
        var entry2 = new LocalizationEntry { SourceText = "Found 250 gold" };
        var entry3 = new LocalizationEntry { SourceText = "Found 500 gold" };

        var analysis = _engine.AnalyzeAndCluster(new[] { entry1, entry2, entry3 });

        // Temsilci girdiyi çevrildi olarak simüle et
        entry1.TranslatedText = "100 altın bulundu";
        entry1.Status = TranslationStatus.Done;

        int propagated = _engine.PropagateClusterTranslations(analysis);

        Assert.Equal(2, propagated);
        Assert.Equal(TranslationStatus.Done, entry2.Status);
        Assert.Equal("250 altın bulundu", entry2.TranslatedText);
        Assert.Equal(TranslationStatus.Done, entry3.Status);
        Assert.Equal("500 altın bulundu", entry3.TranslatedText);
    }
}
