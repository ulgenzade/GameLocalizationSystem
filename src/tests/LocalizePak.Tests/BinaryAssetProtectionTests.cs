using System;
using System.IO;
using System.Threading.Tasks;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats;
using Xunit;

namespace LocalizePak.Tests;

public class BinaryAssetProtectionTests
{
    [Fact]
    public async Task UniversalLocalizationService_RejectsBinaryData_ThrowsException()
    {
        var service = new UniversalLocalizationService();

        // 1. Unreal Engine .uasset benzeri ikili veri (magic: 0x9E2A83C1 + null bytes)
        byte[] fakeUasset = new byte[256];
        fakeUasset[0] = 0xC1;
        fakeUasset[1] = 0x83;
        fakeUasset[2] = 0x2A;
        fakeUasset[3] = 0x9E;
        // Geri kalan byte'larda null byte'lar ve pointer'lar var

        using var ms = new MemoryStream(fakeUasset);

        // İkili dosya kesinlikle metin gibi satırlara ayrılmamalı, InvalidOperationException fırlatmalı!
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.DeserializeAsync(ms);
        });
    }

    [Fact]
    public void PakArchive_LocalizationEntries_DoesNotIncludeBinaryAssets()
    {
        var archive = new PakArchive
        {
            FilePath = @"C:\Games\Poppy\pakchunk0Optional-WindowsNoEditor.pak",
            Entries = new List<PakEntry>
            {
                new() { VirtualPath = "Content/Materials/metal_floor_normal.uasset" },
                new() { VirtualPath = "Content/Materials/metal_floor_normal.uexp" },
                new() { VirtualPath = "Content/Audio/metal_floor_ambience.uasset" }
            }
        };

        // Bu pakette hiç lokalizasyon dosyası olmadığı için LocalizationEntries boş olmalı!
        Assert.Empty(archive.LocalizationEntries);
    }

    [Fact]
    public void PakArchive_LocalizationEntries_IncludesRealLocres()
    {
        var archive = new PakArchive
        {
            FilePath = @"C:\Games\Poppy\pakchunk0-WindowsNoEditor.pak",
            Entries = new List<PakEntry>
            {
                new() { VirtualPath = "Content/Materials/metal_floor_normal.uasset" },
                new() { VirtualPath = "Content/Localization/Game/en/Game.locres" },
                new() { VirtualPath = "Content/Localization/Engine/en/Engine.locres" }
            }
        };

        // Sadece 2 adet .locres dosyası listelenmeli
        Assert.Equal(2, archive.LocalizationEntries.Count);
        Assert.All(archive.LocalizationEntries, e => Assert.True(e.IsLocalizationFile));
    }
}
