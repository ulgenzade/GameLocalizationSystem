using System.IO;
using System.Text;
using CUE4Parse.UE4.Versions;
using LocalizePak.IO.Formats;
using Xunit;

namespace LocalizePak.Tests;

public class EngineVersionCheckTests
{
    [Fact]
    public void VerifyEGameEnumValuesExist()
    {
        Assert.True(Enum.IsDefined(typeof(EGame), EGame.GAME_UE4_27));
        Assert.True(Enum.IsDefined(typeof(EGame), EGame.GAME_UE4_25));
        Assert.True(Enum.IsDefined(typeof(EGame), EGame.GAME_UE4_26));
        Assert.True(Enum.IsDefined(typeof(EGame), EGame.GAME_UE5_0));
        Assert.True(Enum.IsDefined(typeof(EGame), EGame.GAME_UE5_1));
        Assert.True(Enum.IsDefined(typeof(EGame), EGame.GAME_UE5_4));
    }

    [Theory]
    [InlineData(7, EGame.GAME_UE4_21)]
    [InlineData(8, EGame.GAME_UE4_25)]
    [InlineData(9, EGame.GAME_UE4_26)]
    [InlineData(10, EGame.GAME_UE4_27)]
    [InlineData(11, EGame.GAME_UE5_1)]
    [InlineData(12, EGame.GAME_UE5_4)]
    public void MapPakVersionToEGame_MapsCorrectly(int pakVersion, EGame expected)
    {
        var result = UnrealCuePakHandler.MapPakVersionToEGame(pakVersion);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetectEngineVersion_FromV8Pak_DetectsCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gls_test_ver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pakPath = Path.Combine(tempDir, "test.pak");

        try
        {
            using (var fs = File.Create(pakPath))
            {
                UnrealPakWriter.Write(fs, "../../../", [("test.txt", Encoding.UTF8.GetBytes("hello"))]);
            }

            // UnrealPakWriter varsayılan olarak Version 8 yazar
            var detectedVer = UnrealCuePakHandler.DetectEngineVersion(pakPath);
            Assert.Equal(EGame.GAME_UE4_25, detectedVer);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
