using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.Translation.Services;
using LocalizePak.UI.ViewModels;
using Xunit;

namespace LocalizePak.Tests;

public class MvvmModuleTests
{
    [Fact]
    public void ArchiveWorkspaceViewModel_InitialState_Correct()
    {
        var vm = new ArchiveWorkspaceViewModel();
        Assert.False(vm.IsPakLoaded);
        Assert.False(vm.HasLocalizationFiles);
        Assert.False(vm.IsEmptyArchive);
        Assert.Empty(vm.PakEntries);
        Assert.Equal("Açık arşiv yok", vm.ArchiveStatusText);

        vm.IsPakLoaded = true;
        Assert.True(vm.IsEmptyArchive);
        Assert.False(vm.HasLocalizationFiles);

        vm.PakEntries.Add(new PakEntry { VirtualPath = "Game.locres" });
        Assert.True(vm.HasLocalizationFiles);
        Assert.False(vm.IsEmptyArchive);
    }

    [Fact]
    public void TranslationRunViewModel_PendingEntriesCalculation_Correct()
    {
        var vm = new TranslationRunViewModel
        {
            TotalEntries = 100,
            CompletedEntries = 60,
            ErrorCount = 5
        };

        Assert.Equal(35, vm.PendingEntries);
    }

    [Fact]
    public void GridFilterViewModel_Properties_WorkCorrectly()
    {
        var vm = new GridFilterViewModel
        {
            FilterText = "oyun",
            ShowOnlyErrors = true,
            SelectedStatusFilter = "Hatalar"
        };

        Assert.Equal("oyun", vm.FilterText);
        Assert.True(vm.ShowOnlyErrors);
        Assert.Equal("Hatalar", vm.SelectedStatusFilter);
    }

    [Fact]
    public void ExportPatchViewModel_Properties_WorkCorrectly()
    {
        var vm = new ExportPatchViewModel
        {
            LastExportedDirectory = @"C:\Games\Patches",
            HasExportedDirectory = true,
            HasRecoverySession = true,
            RecoverySessionInfo = "Kurtarma var"
        };

        Assert.Equal(@"C:\Games\Patches", vm.LastExportedDirectory);
        Assert.True(vm.HasExportedDirectory);
        Assert.True(vm.HasRecoverySession);
        Assert.Equal("Kurtarma var", vm.RecoverySessionInfo);
    }

    [Fact]
    public void LocalizationEntryViewModel_LengthAndOverflowCalculations_Correct()
    {
        var model = new LocalizationEntry
        {
            Key = "UI_Test_Button",
            SourceText = "Click here to play",
            TranslatedText = "Buraya tıklayarak oyunu başlatabilirsiniz",
            Status = TranslationStatus.Pending
        };

        var vm = new LocalizationEntryViewModel(model);
        Assert.Equal("UI_Test_Button", vm.Key);
        Assert.Equal("Click here to play", vm.SourceText);
        Assert.Equal("Buraya tıklayarak oyunu başlatabilirsiniz", vm.TranslatedText);
        Assert.True(vm.LengthDifferencePercent > 40);
        Assert.True(vm.IsOverflowRisk);
        Assert.Equal("#EF4444", vm.OverflowColor);
    }

    [Fact]
    public void LocalizationEntryViewModel_LqaBadge_UpdatesLiveWhenTranslationEdited()
    {
        var model = new LocalizationEntry
        {
            Key = "UI_Gold",
            SourceText = "Found {0} gold",
            TranslatedText = "Altın buldun",
            Status = TranslationStatus.Done
        };
        var vm = new LocalizationEntryViewModel(model);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(vm.HasQaIssue);
        Assert.Equal(QaSeverity.Critical, vm.HighestQaSeverity);
        Assert.Equal("#EF4444", vm.QaBadgeColor);
        Assert.StartsWith("LQA: 1 sorun (en yüksek: Kritik)", vm.QaTooltip);

        vm.TranslatedText = "{0} altın buldun";

        Assert.False(vm.HasQaIssue);
        Assert.Null(vm.HighestQaSeverity);
        Assert.Equal(string.Empty, vm.QaTooltip);
        Assert.Contains(nameof(LocalizationEntryViewModel.HasQaIssue), changed);
        Assert.Contains(nameof(LocalizationEntryViewModel.QaBadgeColor), changed);
    }

    [Fact]
    public void LocalizationEntryViewModel_LqaBadge_UntranslatedContentIsHighSeverity()
    {
        var model = new LocalizationEntry
        {
            Key = "DLG_1",
            SourceText = "Welcome to the kingdom",
            TranslatedText = "Welcome to the kingdom"
        };

        var vm = new LocalizationEntryViewModel(model);

        Assert.Equal(QaSeverity.High, vm.HighestQaSeverity);
        Assert.Equal("#F97316", vm.QaBadgeColor);
        Assert.Equal(1, vm.QaIssueCount);
    }
}
