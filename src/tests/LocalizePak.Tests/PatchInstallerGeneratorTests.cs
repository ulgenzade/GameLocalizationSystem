using System;
using System.IO;
using LocalizePak.IO.Services;
using Xunit;

namespace LocalizePak.Tests;

public class PatchInstallerGeneratorTests
{
    [Fact]
    public void GenerateInstallerBatch_ContainsFsekDisclaimerAndGameName()
    {
        string script = PatchInstallerGenerator.GenerateInstallerBatch("Witcher 3", "witcher3_tr_P.pak");

        Assert.Contains("Witcher 3", script);
        Assert.Contains("witcher3_tr_P.pak", script);
        Assert.Contains("5846", script);
        Assert.Contains("FSEK", script);
        Assert.Contains(".bak", script); // Yedekleme mantığı
    }

    [Fact]
    public void GenerateUninstallerBatch_ContainsRestoreLogic()
    {
        string script = PatchInstallerGenerator.GenerateUninstallerBatch("Witcher 3", "witcher3_tr_P.pak");

        Assert.Contains("del /f /q", script);
        Assert.Contains(".bak", script);
        Assert.Contains("move /y", script); // Orijinal yedeği geri taşıma mantığı
    }

    [Fact]
    public void CreateDistributionPackage_GeneratesAllRequiredFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"dist_test_{Guid.NewGuid():N}");

        try
        {
            byte[] dummyPak = new byte[] { 0x50, 0x41, 0x4B, 0x01 };
            PatchInstallerGenerator.CreateDistributionPackage(tempDir, "Elden Ring", "elden_ring_tr_P.pak", dummyPak);

            Assert.True(File.Exists(Path.Combine(tempDir, "elden_ring_tr_P.pak")));
            Assert.True(File.Exists(Path.Combine(tempDir, "Kurulum_ve_Yedekle.bat")));
            Assert.True(File.Exists(Path.Combine(tempDir, "Yamayi_Kaldir_Orijinale_Don.bat")));
            Assert.True(File.Exists(Path.Combine(tempDir, "BENI_OKU_VE_LISANS.txt")));

            string readme = File.ReadAllText(Path.Combine(tempDir, "BENI_OKU_VE_LISANS.txt"));
            Assert.Contains("Elden Ring", readme);
            Assert.Contains("5846", readme);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
