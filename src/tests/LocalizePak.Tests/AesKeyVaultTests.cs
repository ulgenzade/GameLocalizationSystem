using System;
using System.IO;
using System.Linq;
using LocalizePak.Core.Models;
using LocalizePak.IO.Services;
using Xunit;

namespace LocalizePak.Tests;

public class AesKeyVaultTests
{
    [Fact]
    public void GetAllKeys_ContainsBuiltInGames()
    {
        var keys = AesKeyVaultService.GetAllKeys();
        Assert.NotNull(keys);
        Assert.NotEmpty(keys);

        // Poppy Playtime, Hogwarts Legacy, Silent Hill 2, Tekken 8 gömülü olmalı
        Assert.Contains(keys, k => k.GameName.Contains("Poppy Playtime", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keys, k => k.GameName.Contains("Hogwarts Legacy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keys, k => k.GameName.Contains("Silent Hill 2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keys, k => k.GameName.Contains("Tekken 8", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_FiltersKeysCorrectly()
    {
        var results = AesKeyVaultService.Search("poppy");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains("poppy", r.GameName.ToLowerInvariant()));
    }

    [Fact]
    public void FindKeyForPath_MatchesGamePathCorrectly()
    {
        // Poppy Playtime pak yolu simülasyonu
        string samplePath = @"D:\Games\Poppy Playtime\Playtime_Prototype4\Content\Paks\pakchunk0-WindowsNoEditor.pak";
        var match = AesKeyVaultService.FindKeyForPath(samplePath);

        Assert.NotNull(match);
        Assert.Contains("Poppy Playtime", match.GameName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0xD4BCF215F3B33A4BAA8D52139F1F49E92DF5BF8C7262C823E846AA6D79331FBC", match.AesKey);
    }

    [Fact]
    public void FindKeyForPath_IgnoresWordsInUserFolders()
    {
        // "Desktop\GL System\..." eskiden "System Shock Remake" anahtarını seçtiriyordu; şifresiz oyunun yaması
        // bu anahtarla şifrelenip oyunda hiç açılmıyordu.
        var match = AesKeyVaultService.FindKeyForPath(
            @"C:\Users\Alper\Desktop\GL System\kopya\ProSoccerOnline\ProSoccerOnline\Content\Paks\pakchunk0-Windows.utoc");
        Assert.Null(match);
    }

    [Fact]
    public void AddOrUpdateKey_CustomKeyPersistsAndIsSearchable()
    {
        string uniqueGameName = "Test Game " + Guid.NewGuid().ToString("N")[..8];
        string testKey = "0x11223344556677889900AABBCCDDEEFF11223344556677889900AABBCCDDEEFF";

        var newEntry = new AesKeyVaultEntry
        {
            GameName = uniqueGameName,
            AesKey = testKey,
            EngineVersion = "UE 5.4",
            PakPattern = "TestGame",
            IsCustom = true
        };

        AesKeyVaultService.AddOrUpdateKey(newEntry);

        var searchResult = AesKeyVaultService.Search(uniqueGameName);
        Assert.Single(searchResult);
        Assert.Equal(testKey, searchResult[0].AesKey);

        // Path eşleşmesi kontrolü
        var match = AesKeyVaultService.FindKeyForPath(@"C:\Games\TestGame\Paks\pakchunk0.pak");
        Assert.NotNull(match);
        Assert.Equal(uniqueGameName, match.GameName);
    }
}
