using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Versions;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Formats.IoStore;
using Xunit;

namespace LocalizePak.Tests;

public class IoStoreTests
{
    // ─── Yardımcılar ─────────────────────────────────────────────────────────

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gls_iostore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFString(BinaryWriter w, string s, bool utf16 = false)
    {
        if (s.Length == 0) { w.Write(0); return; }
        if (utf16)
        {
            w.Write(-(s.Length + 1));
            w.Write(Encoding.Unicode.GetBytes(s));
            w.Write((ushort)0);
        }
        else
        {
            w.Write(s.Length + 1);
            w.Write(Encoding.Latin1.GetBytes(s));
            w.Write((byte)0);
        }
    }

    /// <summary>Sürümsüz özellik verisi içinde bir FText (Base geçmişi) serileştirmesi.</summary>
    private static byte[] FTextBytes(string ns, string key, string source, int flags = 0, bool utf16 = false)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(flags);
        w.Write((byte)0); // ETextHistoryType::Base
        WriteFString(w, ns);
        WriteFString(w, key);
        WriteFString(w, source, utf16);
        return ms.ToArray();
    }

    private static byte[] Package(params byte[][] parts)
    {
        // Gerçek paketlerdeki gibi metinlerin etrafında ikili gürültü
        var rnd = new Random(42);
        using var ms = new MemoryStream();
        foreach (var part in parts)
        {
            var noise = new byte[rnd.Next(8, 40)];
            rnd.NextBytes(noise);
            ms.Write(noise);
            ms.Write(part);
        }
        ms.Write(new byte[16]);
        return ms.ToArray();
    }

    // ─── Metin hash'i ────────────────────────────────────────────────────────

    /// <summary>FCrc::StrCrc32'nin bağımsız (bit bit) başvuru uygulaması.</summary>
    private static uint ReferenceStrCrc32(string s)
    {
        uint crc = 0xFFFFFFFF;
        foreach (char ch in s)
        {
            uint c = ch;
            for (int b = 0; b < 4; b++)
            {
                crc ^= c & 0xFF;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                c >>= 8;
            }
        }
        return ~crc;
    }

    [Theory]
    [InlineData("")]
    [InlineData("Play")]
    [InlineData("Welcome to the tutorial!")]
    [InlineData("Türkçe: ğüşıöç İĞÜŞÖÇ")]
    [InlineData("中文 / 日本語")]
    public void StrCrc32_MatchesReferenceImplementation(string text)
    {
        Assert.Equal(ReferenceStrCrc32(text), UnrealTextHash.StrCrc32(text));
    }

    [Fact]
    public void StrCrc32_HandlesLongStringsAcrossBufferBoundary()
    {
        var text = new string('a', 300) + "ğ" + new string('b', 700);
        Assert.Equal(ReferenceStrCrc32(text), UnrealTextHash.StrCrc32(text));
    }

    // ─── Tarayıcı ────────────────────────────────────────────────────────────

    [Fact]
    public void Scanner_FindsBaseTexts_AnsiAndUtf16()
    {
        var data = Package(
            FTextBytes("", "29CA413B417625978B455692661A2AEF", "Team Name"),
            FTextBytes("Menu", "Play", "Oyna ğüş", utf16: true));

        var texts = ZenTextScanner.Scan(data);

        Assert.Contains(new GatheredText("", "29CA413B417625978B455692661A2AEF", "Team Name", false), texts);
        Assert.Contains(new GatheredText("Menu", "Play", "Oyna ğüş", false), texts);
    }

    [Fact]
    public void Scanner_SkipsCultureInvariantAndUnknownFlags()
    {
        var data = Package(
            FTextBytes("", "A1", "Invariant", flags: 0x2),
            FTextBytes("", "A2", "Bad flags", flags: 0x100));

        Assert.Empty(ZenTextScanner.Scan(data));
    }

    [Fact]
    public void Scanner_FindsStringTableEntries()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            WriteFString(w, "ST_Common");
            w.Write(3);
            WriteFString(w, "Arena"); WriteFString(w, "Arena");
            WriteFString(w, "Back"); WriteFString(w, "Back");
            WriteFString(w, "Confirm"); WriteFString(w, "Are you sure?");
            w.Write(0); // meta veri haritası
        }

        var texts = ZenTextScanner.Scan(Package(ms.ToArray()));

        Assert.Equal(3, texts.Count(t => t.FromStringTable && t.Namespace == "ST_Common"));
        Assert.Contains(new GatheredText("ST_Common", "Confirm", "Are you sure?", true), texts);
    }

    [Fact]
    public void Scanner_FindsBlueprintTextLiterals()
    {
        // EX_TextConst · LocalizedText · EX_StringConst kaynak · EX_StringConst anahtar · EX_UnicodeStringConst ad alanı
        using var ms = new MemoryStream();
        ms.WriteByte(0x29);
        ms.WriteByte(1);
        ms.WriteByte(0x1F); ms.Write(Encoding.Latin1.GetBytes("Cannot kick admins")); ms.WriteByte(0);
        ms.WriteByte(0x1F); ms.Write(Encoding.Latin1.GetBytes("B25F2D0547AA03099AAB8BA8BB5D27D8")); ms.WriteByte(0);
        ms.WriteByte(0x34); ms.Write(Encoding.Unicode.GetBytes("")); ms.Write(new byte[2]);

        var texts = ZenTextScanner.Scan(Package(ms.ToArray()));

        Assert.Contains(new GatheredText("", "B25F2D0547AA03099AAB8BA8BB5D27D8", "Cannot kick admins", false), texts);
    }

    [Fact]
    public void Scanner_FindsNothingInRandomBinary()
    {
        var rnd = new Random(7);
        var data = new byte[256 * 1024];
        rnd.NextBytes(data);
        Assert.Empty(ZenTextScanner.Scan(data));
    }

    // ─── Toplayıcı ───────────────────────────────────────────────────────────

    [Fact]
    public void Gatherer_DeduplicatesSkipsEngineAndProducesValidLocres()
    {
        var packages = new List<(string, Func<byte[]>)>
        {
            ("Game/Content/UI/W_Menu.uasset", () => Package(FTextBytes("", "K1", "Play"), FTextBytes("", "K2", "Quit"))),
            ("Game/Content/UI/W_Pause.uasset", () => Package(FTextBytes("", "K1", "Play"))),
            ("Engine/Content/Editor/E.uasset", () => Package(FTextBytes("", "E1", "Engine text"))),
            ("Game/Content/Broken.uasset", () => throw new IOException("bozuk")),
        };

        var file = IoStoreTextGatherer.Gather(packages);
        var entries = file.AllEntries.ToList();

        Assert.Equal(new[] { "K1", "K2" }, entries.Select(e => e.Key));
        Assert.All(entries, e => Assert.Equal(UnrealTextHash.StrCrc32(e.SourceText), e.SourceStringHash));
        Assert.All(entries, e => Assert.Equal(TranslationStatus.Pending, e.Status));

        // Üretilen .locres (V2) geri okunabilir; çeviri yazılınca korunur
        entries[0].TranslatedText = "Oyna";
        var reread = LocresCodec.Read(LocresCodec.Write(file));
        Assert.Equal(LocresVersion.V2, reread.Version);
        var play = reread.AllEntries.Single(e => e.Key == "K1");
        Assert.Equal("Oyna", play.TranslatedText);
        Assert.Equal(UnrealTextHash.StrCrc32("Play"), play.SourceStringHash);
    }

    [Fact]
    public void GeneratedLocresPath_OnlyWhenGameHasNoLocres()
    {
        var packagesOnly = new List<PakEntry>
        {
            new() { VirtualPath = "Engine/Content/Localization/Engine/en/Engine.locres" },
            // Motor eklentisi .locres'ı (başta "/" olmadan) oyun dosyası sayılmamalı
            new() { VirtualPath = "Engine/Plugins/Online/OnlineSubsystem/Content/Localization/OnlineSubsystem/en/OnlineSubsystem.locres" },
            new() { VirtualPath = "ProSoccerOnline/Content/UI/W_Menu.uasset" },
        };
        Assert.Equal("ProSoccerOnline/Content/Localization/Game/en/Game.locres", UnrealCuePakHandler.GeneratedLocresPath(packagesOnly));

        var withGameLocres = new List<PakEntry>(packagesOnly) { new() { VirtualPath = "ProSoccerOnline/Content/Localization/Game/en/Game.locres" } };
        Assert.Null(UnrealCuePakHandler.GeneratedLocresPath(withGameLocres));

        var engineOnly = new List<PakEntry> { new() { VirtualPath = "Engine/Content/Editor/E.uasset" } };
        Assert.Null(UnrealCuePakHandler.GeneratedLocresPath(engineOnly));
    }

    // ─── Konteyner yazıcısı (CUE4Parse ile geri okunarak doğrulanır) ─────────

    public static IEnumerable<object[]> ContainerVersions() =>
    [
        [EIoStoreTocVersion.PerfectHashWithOverflow, EIoContainerHeaderVersion.Initial, EGame.GAME_UE5_1],
        [EIoStoreTocVersion.RemovedOnDemandMetaData, EIoContainerHeaderVersion.NoExportInfo, EGame.GAME_UE5_3],
        [EIoStoreTocVersion.ReplaceIoChunkHashWithIoHash, EIoContainerHeaderVersion.SoftPackageReferences, EGame.GAME_UE5_4],
        [EIoStoreTocVersion.ReplaceIoChunkHashWithIoHash, EIoContainerHeaderVersion.SoftPackageReferencesOffset, EGame.GAME_UE5_5],
    ];

    [Theory]
    [MemberData(nameof(ContainerVersions))]
    public void ContainerWriter_OutputIsReadableByCUE4Parse(EIoStoreTocVersion tocVersion, EIoContainerHeaderVersion headerVersion, EGame game)
    {
        var dir = NewTempDir();
        try
        {
            var big = new byte[1000];
            new Random(1).NextBytes(big);
            var package = Encoding.UTF8.GetBytes("zen package bytes");
            var chunks = new List<IoStoreChunk>
            {
                new() { Id = 0x1111, Type = EIoChunkType5.ExportBundleData, Data = package, Path = "Game/Content/UI/W_Menu.uasset",
                        Package = new IoStorePackageInfo { PackageId = 0x1111, ImportedPackages = [0xAAAA, 0xBBBB], ExportCount = 3, ExportBundleCount = 1 } },
                new() { Id = 0x1111, Type = EIoChunkType5.BulkData, Data = big, Path = "Game/Content/UI/W_Menu.ubulk" },
                new() { Id = 0x2222, Type = EIoChunkType5.ExportBundleData, Data = [1, 2, 3], Path = "Game/Content/Maps/L_Main.umap",
                        Package = new IoStorePackageInfo { PackageId = 0x2222 } },
            };
            var basePath = Path.Combine(dir, "pakchunk0-Windows_P");

            // Küçük blok boyutu: 1000 baytlık parça birden çok bloğa bölünür
            IoStoreContainerWriter.Write(basePath, chunks, tocVersion, headerVersion, compressionBlockSize: 256);

            using var reader = new IoStoreReader(basePath + ".utoc", EIoStoreTocReadOptions.ReadAll, new VersionContainer(game));
            reader.Mount(StringComparer.OrdinalIgnoreCase);

            Assert.Equal(tocVersion, reader.TocResource.Header.Version);
            Assert.Equal(IoStoreContainerWriter.ContainerIdFromName("pakchunk0-Windows_P"), reader.TocResource.Header.ContainerId.Id);
            Assert.Equal(4, reader.TocResource.ChunkMetas!.Length); // 3 parça + konteyner başlığı
            Assert.Equal(3, reader.Files.Count);
            Assert.Equal(package, reader.Files["Game/Content/UI/W_Menu.uasset"].Read());
            Assert.Equal(big, reader.Files["Game/Content/UI/W_Menu.ubulk"].Read());
            Assert.Equal(new byte[] { 1, 2, 3 }, reader.Files["Game/Content/Maps/L_Main.umap"].Read());

            var header = reader.ContainerHeader!;
            Assert.Equal(headerVersion, header.Version);
            Assert.Equal(new[] { new FPackageId(0x1111), new FPackageId(0x2222) }, header.PackageIds);
            Assert.Equal(new[] { new FPackageId(0xAAAA), new FPackageId(0xBBBB) }, header.StoreEntries[0].ImportedPackages);
            Assert.Empty(header.StoreEntries[1].ImportedPackages);
            if (headerVersion < EIoContainerHeaderVersion.NoExportInfo)
            {
                Assert.Equal(3, header.StoreEntries[0].ExportCount);
                Assert.Equal(1, header.StoreEntries[0].ExportBundleCount);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ContainerWriter_RejectsDuplicateChunkIds()
    {
        var dir = NewTempDir();
        try
        {
            var chunks = new List<IoStoreChunk>
            {
                new() { Id = 1, Type = EIoChunkType5.BulkData, Data = [1] },
                new() { Id = 1, Type = EIoChunkType5.BulkData, Data = [2] },
            };
            Assert.Throws<ArgumentException>(() => IoStoreContainerWriter.Write(Path.Combine(dir, "x_P"), chunks,
                EIoStoreTocVersion.ReplaceIoChunkHashWithIoHash, EIoContainerHeaderVersion.SoftPackageReferences));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadTocVersion_DetectsUtocOnly()
    {
        var dir = NewTempDir();
        try
        {
            var basePath = Path.Combine(dir, "a_P");
            IoStoreContainerWriter.Write(basePath, [], EIoStoreTocVersion.RemovedOnDemandMetaData, EIoContainerHeaderVersion.NoExportInfo);

            using (var toc = File.OpenRead(basePath + ".utoc"))
            {
                Assert.True(UnrealCuePakHandler.IsIoStoreToc(toc));
                Assert.Equal((int)EIoStoreTocVersion.RemovedOnDemandMetaData, UnrealCuePakHandler.ReadTocVersion(toc));
            }
            using (var cas = File.OpenRead(basePath + ".ucas"))
                Assert.False(UnrealCuePakHandler.IsIoStoreToc(cas));

            Assert.Equal(EGame.GAME_UE5_3, UnrealCuePakHandler.MapTocVersionToEGame((int)EIoStoreTocVersion.RemovedOnDemandMetaData));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ─── Uçtan uca: .pak + .utoc aç, metinleri topla, yama ve eşini yaz ─────

    [Fact]
    public async Task OpenIoStoreGame_GathersTexts_AndPatchCompanionMounts()
    {
        var dir = NewTempDir();
        var outDir = NewTempDir();
        try
        {
            // Oyun: pakchunk0-Windows.pak (gevşek dosyalar) + pakchunk0-Windows.utoc/.ucas (paketler), .locres yok
            using (var fs = File.Create(Path.Combine(dir, "pakchunk0-Windows.pak")))
                UnrealPakWriter.Write(fs, "../../../", [("MyGame/Config/DefaultGame.ini", Encoding.UTF8.GetBytes("[/Script]"))]);

            var menu = Package(FTextBytes("", "29CA413B417625978B455692661A2AEF", "Start Match"), FTextBytes("", "K2", "Options"));
            IoStoreContainerWriter.Write(Path.Combine(dir, "pakchunk0-Windows"),
            [
                new IoStoreChunk { Id = 0x10, Type = EIoChunkType5.ExportBundleData, Data = menu, Path = "MyGame/Content/UI/W_Menu.uasset",
                                   Package = new IoStorePackageInfo { PackageId = 0x10 } },
            ], EIoStoreTocVersion.ReplaceIoChunkHashWithIoHash, EIoContainerHeaderVersion.SoftPackageReferences);

            var service = new PakService();
            var archive = await service.OpenAsync(Path.Combine(dir, "pakchunk0-Windows.utoc"));

            Assert.True(archive.IsIoStore);
            Assert.Equal((int)EIoStoreTocVersion.ReplaceIoChunkHashWithIoHash, archive.IoStoreTocVersion);
            Assert.Contains(archive.Entries, e => e.VirtualPath == "MyGame/Config/DefaultGame.ini");
            Assert.Contains(archive.Entries, e => e.VirtualPath == "MyGame/Content/UI/W_Menu.uasset");

            var generated = Assert.Single(archive.LocalizationEntries, e => e.IsGenerated);
            Assert.Equal("MyGame/Content/Localization/Game/en/Game.locres", generated.VirtualPath);

            await using var stream = await service.ExtractEntryAsync(archive, generated);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var locres = LocresCodec.Read(ms.ToArray());
            Assert.Equal(new[] { "Start Match", "Options" }, locres.AllEntries.Select(e => e.SourceText));

            // Yama: _P.pak + _P.utoc/_P.ucas eşi; CUE4Parse ikisini de bağlar
            var patchPath = Path.Combine(outDir, "pakchunk0-Windows_P.pak");
            await service.WriteAsync(patchPath, archive.MountPoint, [(generated.VirtualPath, ms.ToArray())], sourceFormat: PakFormat.Unreal);
            await PakService.WriteIoStoreCompanionAsync(patchPath, archive);

            Assert.True(File.Exists(Path.Combine(outDir, "pakchunk0-Windows_P.utoc")));
            Assert.True(File.Exists(Path.Combine(outDir, "pakchunk0-Windows_P.ucas")));

            var provider = new DefaultFileProvider(outDir, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE5_4), StringComparer.OrdinalIgnoreCase);
            provider.Initialize();
            provider.Mount();
            Assert.Equal(2, provider.MountedVfs.Count);
            var companion = provider.MountedVfs.OfType<IoStoreReader>().Single();
            Assert.Empty(companion.Files);
            Assert.Empty(companion.ContainerHeader!.PackageIds);
            Assert.Equal(ms.ToArray(), provider.Files[generated.VirtualPath].Read());
        }
        finally
        {
            Directory.Delete(dir, true);
            Directory.Delete(outDir, true);
        }
    }
}
