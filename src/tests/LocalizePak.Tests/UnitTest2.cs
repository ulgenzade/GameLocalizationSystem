using System.Security.Cryptography;
using System.Text;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Helpers;
using LocalizePak.IO.Services;

namespace LocalizePak.Tests;

/// <summary>.locres codec, UE yama pak yazıcısı ve statik AES tarayıcı testleri (platformdan bağımsız).</summary>
public class UnrealFormatTests
{
    private static LocresFile SampleLocres(LocresVersion v)
    {
        var f = new LocresFile { Version = v };
        var ns = new LocresNamespace { Name = "Game", NameHash = 0xAABBCCDD };
        ns.Entries.Add(new LocalizationEntry { NamespaceName = "Game", Key = "KEY_A", KeyHash = 0x11111111, SourceStringHash = 0x22222222, SourceText = "Play", TranslatedText = "Play" });
        ns.Entries.Add(new LocalizationEntry { NamespaceName = "Game", Key = "KEY_B", KeyHash = 0x33333333, SourceStringHash = 0x44444444, SourceText = "Settings", TranslatedText = "Ayarlar" });
        ns.Entries.Add(new LocalizationEntry { NamespaceName = "Game", Key = "KEY_C", KeyHash = 0x55555555, SourceStringHash = 0x66666666, SourceText = "Quit", TranslatedText = "Çıkış: öğüşıİ" });
        // Aynı metin iki kez: tablo tekilleştirilmeli
        ns.Entries.Add(new LocalizationEntry { NamespaceName = "Game", Key = "KEY_D", KeyHash = 0x77777777, SourceStringHash = 0x88888888, SourceText = "Basic", TranslatedText = "Temel" });
        ns.Entries.Add(new LocalizationEntry { NamespaceName = "Game", Key = "KEY_E", KeyHash = 0x99999999, SourceStringHash = 0x88888888, SourceText = "Basic", TranslatedText = "Temel" });
        f.Namespaces.Add(ns);
        return f;
    }

    [Theory]
    [InlineData(LocresVersion.Legacy)]
    [InlineData(LocresVersion.V1)]
    [InlineData(LocresVersion.V2)]
    [InlineData(LocresVersion.V3)]
    public void LocresCodec_Roundtrip_PreservesKeysAndTurkishText(LocresVersion v)
    {
        var bytes = LocresCodec.Write(SampleLocres(v));
        var back = LocresCodec.Read(bytes);

        Assert.Equal(v, back.Version);
        var e = back.AllEntries.ToList();
        Assert.Equal(5, e.Count);
        Assert.Equal("Ayarlar", e[1].SourceText);
        Assert.Equal("Çıkış: öğüşıİ", e[2].SourceText);
        Assert.Equal(new[] { "KEY_A", "KEY_B", "KEY_C", "KEY_D", "KEY_E" }, e.Select(x => x.Key));
        Assert.All(e, x => Assert.True(x.SourceStringHash != 0));
        if (v == LocresVersion.V3)
        {
            Assert.Equal(0xAABBCCDDu, back.Namespaces[0].NameHash);
            Assert.Equal(0x33333333u, e[1].KeyHash); // hash'ler korunmalı
        }
    }

    [Fact]
    public void LocresCodec_V3_IsReadableByCUE4Parse()
    {
        // Yazdığımız dosya referans UE ayrıştırıcısı (CUE4Parse) tarafından da aynı içerikle okunabilmeli
        var bytes = LocresCodec.Write(SampleLocres(LocresVersion.V3));
        var ar = new CUE4Parse.UE4.Readers.FByteArchive("t.locres", bytes);
        var res = new CUE4Parse.UE4.Localization.FTextLocalizationResource(ar);

        var all = res.Entries.SelectMany(n => n.Value.Select(k => (Ns: n.Key.Str, Key: k.Key.Str, Text: k.Value.LocalizedString))).ToList();
        Assert.Equal(5, all.Count);
        Assert.Contains(all, x => x.Key == "KEY_C" && x.Text == "Çıkış: öğüşıİ");
        Assert.Contains(all, x => x.Key == "KEY_A" && x.Text == "Play");
    }

    [Fact]
    public void LocresCodec_FString_AsciiUsesPositiveLength_NonAsciiUsesUtf16()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            LocresCodec.WriteFString(bw, "abc");
            LocresCodec.WriteFString(bw, "ğ");
            LocresCodec.WriteFString(bw, "");
        }
        var b = ms.ToArray();
        Assert.Equal(4, BitConverter.ToInt32(b, 0));          // "abc\0"
        Assert.Equal(-2, BitConverter.ToInt32(b, 8));         // "ğ\0" UTF-16
    }

    [Fact]
    public void AesHelper_Ecb_RoundtripAndKnownVector()
    {
        var key = Convert.FromHexString("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");
        var data = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        // FIPS-197 C.3 AES-256 test vektörü
        Assert.Equal("8EA2B7CA516745BFEAFC49904B496089", Convert.ToHexString(AesDecryptionHelper.EncryptEcb(data, key)));
        Assert.Equal(data, AesDecryptionHelper.DecryptEcb(AesDecryptionHelper.EncryptEcb(data, key), key));
    }

    private static (string Dir, string Pak) NewTempPak() 
    {
        var dir = Path.Combine(Path.GetTempPath(), "gls_pak_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, Path.Combine(dir, "pakchunk0-WindowsNoEditor_P.pak"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrealPakWriter_Output_IsReadableByCUE4Parse(bool encryptIndex)
    {
        var (dir, pak) = NewTempPak();
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var locres = LocresCodec.Write(SampleLocres(LocresVersion.V3));
            var files = new List<(string, byte[])>
            {
                ("Poppy_Playtime/Content/Localization/Game/pt/Game.locres", locres),
                ("Poppy_Playtime/Content/Other/readme.txt", Encoding.UTF8.GetBytes("merhaba")),
            };
            using (var fs = File.Create(pak)) UnrealPakWriter.Write(fs, "../../../", files, encryptIndex ? key : null);

            var provider = new DefaultFileProvider(dir, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE4_27), StringComparer.Ordinal);
            provider.Initialize();
            if (encryptIndex) provider.SubmitKey(new FGuid(), new FAesKey(key));
            else provider.Mount();

            Assert.Equal(2, provider.Files.Count);
            var data = provider.Files.First(f => f.Key.EndsWith("Game.locres", StringComparison.OrdinalIgnoreCase)).Value.Read();
            Assert.Equal(locres, data);
            Assert.Equal("merhaba", Encoding.UTF8.GetString(provider.Files.First(f => f.Key.EndsWith("readme.txt", StringComparison.OrdinalIgnoreCase)).Value.Read()));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PakIndexKeyTester_AcceptsRightKey_RejectsWrongKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var (dir, pak) = NewTempPak();
        try
        {
            using (var fs = File.Create(pak)) UnrealPakWriter.Write(fs, "../../../", [("a/b.txt", [1, 2, 3])], key);
            var head = PakIndexKeyTester.ReadEncryptedIndexHead(pak);
            Assert.NotNull(head);
            using var t = new PakIndexKeyTester(head!);
            Assert.True(t.Test(key));
            Assert.False(t.Test(RandomNumberGenerator.GetBytes(32)));

            // Şifresiz pak için baş okunmaz
            var (d2, p2) = NewTempPak();
            try
            {
                using (var fs = File.Create(p2)) UnrealPakWriter.Write(fs, "../../../", [("a/b.txt", [1])], null);
                Assert.Null(PakIndexKeyTester.ReadEncryptedIndexHead(p2));
            }
            finally { Directory.Delete(d2, true); }
        }
        finally { Directory.Delete(dir, true); }
    }

    private static byte[] FakeExeWithStackKey(byte[] key, bool ascending)
    {
        // 8 adet: mov dword ptr [rsp+disp8], imm32   (C7 44 24 disp8 imm32)
        var ms = new MemoryStream();
        ms.Write(new byte[4096]);                      // dolgu
        ms.Write([0x48, 0x83, 0xEC, 0x58]);            // sub rsp, 58h
        for (int i = 0; i < 8; i++)
        {
            int slot = ascending ? i : 7 - i;
            ms.Write([0xC7, 0x44, 0x24, (byte)(0x20 + slot * 4)]);
            ms.Write(key.AsSpan(slot * 4, 4));
        }
        ms.Write(new byte[4096]);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaticAesKeyScanner_FindsKeyBuiltFromStackImmediates(bool ascending)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var (dir, pak) = NewTempPak();
        try
        {
            using (var fs = File.Create(pak)) UnrealPakWriter.Write(fs, "../../../", [("a/b.txt", [1, 2, 3])], key);
            var exe = Path.Combine(dir, "game.exe");
            File.WriteAllBytes(exe, FakeExeWithStackKey(key, ascending));

            var r = StaticAesKeyScanner.ScanFile(exe, pak);
            Assert.NotNull(r);
            Assert.Equal("0x" + Convert.ToHexString(key), r!.KeyHex);
            Assert.Equal("stack-imm32", r.Method);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StaticAesKeyScanner_FindsContiguousKey_AndReturnsNullWhenAbsent()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var (dir, pak) = NewTempPak();
        try
        {
            using (var fs = File.Create(pak)) UnrealPakWriter.Write(fs, "../../../", [("a/b.txt", [1, 2, 3])], key);
            var head = PakIndexKeyTester.ReadEncryptedIndexHead(pak)!;

            var noise = RandomNumberGenerator.GetBytes(300_000);
            Buffer.BlockCopy(key, 0, noise, 123_457, 32);   // hizasız ofset
            var r = StaticAesKeyScanner.ScanBuffer(noise, head);
            Assert.NotNull(r);
            Assert.Equal("contiguous", r!.Method);
            Assert.Equal(123_457, r.FileOffset);

            Assert.Null(StaticAesKeyScanner.ScanBuffer(RandomNumberGenerator.GetBytes(300_000), head));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task PakService_Extract_DoesNotReturnGarbageWhenCue4ParseFails()
    {
        // CUE4Parse'ın çıkaramadığı (DataOffset=0) bir girdide pak'ın ilk baytları "dosya" diye dönmemeli
        var (dir, pak) = NewTempPak();
        try
        {
            using (var fs = File.Create(pak)) UnrealPakWriter.Write(fs, "../../../", [("a/b.txt", [1, 2, 3])], null);
            var svc = new PakService();
            var archive = await svc.OpenAsync(pak);
            var ghost = new PakEntry { VirtualPath = "yok/olmayan.txt", UncompressedSize = 3, CompressedSize = 3, DataOffset = 0 };
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExtractEntryAsync(archive, ghost));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class UnencryptedPakTests
{
    [Fact]
    public async Task PakService_OpensAndExtractsUnencryptedUnrealPak()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gls_plain_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var pak = Path.Combine(dir, "plain_P.pak");
        try
        {
            var svc = new PakService();
            await svc.WriteAsync(pak, "../../../", [("Game/Content/x.txt", Encoding.UTF8.GetBytes("selam"))], default, PakFormat.Unreal);

            var archive = await svc.OpenAsync(pak);          // anahtar yok
            Assert.Equal(PakFormat.Unreal, archive.Format);
            Assert.Single(archive.Entries);
            using var s = await svc.ExtractEntryAsync(archive, archive.Entries[0]);
            Assert.Equal("selam", new StreamReader(s).ReadToEnd());
        }
        finally { Directory.Delete(dir, true); }
    }
}
