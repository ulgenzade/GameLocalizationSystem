using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
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
}
