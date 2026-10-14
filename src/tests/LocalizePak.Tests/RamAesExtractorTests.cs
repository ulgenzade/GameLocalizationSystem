using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LocalizePak.IO;
using LocalizePak.IO.Helpers;
using LocalizePak.IO.Services;
using Xunit;

namespace LocalizePak.Tests;

public class RamAesExtractorTests
{
    [Fact]
    public void GetRunningProcesses_ReturnsCurrentAndUserProcesses()
    {
        var list = RamAesExtractor.GetRunningProcesses();
        Assert.NotNull(list);
        Assert.NotEmpty(list);
    }

    [Fact]
    public async Task ScanProcess_FindsAesKeyInProcessMemory()
    {
        // 1. Rastgele 256-bit test anahtarı oluştur
        byte[] testKeyBytes = new byte[32];
        RandomNumberGenerator.Fill(testKeyBytes);
        string expectedHexKey = "0x" + Convert.ToHexString(testKeyBytes);

        // 2. Geçerli bir Unreal Pak Index başlığı simüle et (MountPoint = "../../../")
        byte[] rawIndexHead = new byte[32];
        // mountLen = 10 (int32)
        BitConverter.GetBytes(10).CopyTo(rawIndexHead, 0);
        // "../../../\0"
        Encoding.ASCII.GetBytes("../../../\0").CopyTo(rawIndexHead, 4);

        // AES-ECB ile şifrele
        using var aes = Aes.Create();
        aes.Key = testKeyBytes;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        byte[] encryptedHead = encryptor.TransformFinalBlock(rawIndexHead, 0, 32);

        // 3. Geçici bir sahte .pak dosyası oluştur (footer + encrypted index head)
        string tempPak = Path.Combine(Path.GetTempPath(), $"test_pak_{Guid.NewGuid()}.pak");
        try
        {
            using (var fs = new FileStream(tempPak, FileMode.Create, FileAccess.Write))
            {
                // Dosya başına biraz rastgele veri
                fs.Write(new byte[1024]);

                // Index konumu
                long indexOffset = fs.Position;
                fs.Write(encryptedHead);

                // Footer (Magic: 0x5A6F12E1)
                // Offset: Magic(4) + Version(4) + IndexOffset(8) + IndexSize(8) + SHA1(20) + Encrypted(1)
                fs.Write(new byte[100]); // Boşluk
                using var bw = new BinaryWriter(fs);
                bw.Write(0x5A6F12E1u); // Magic
                bw.Write(11);          // Version
                bw.Write(indexOffset); // IndexOffset
                bw.Write(32L);         // IndexSize
                bw.Write(new byte[20]); // SHA1
                bw.Write((byte)1);     // IsEncrypted
            }

            // 4. Test anahtarını mevcut sürecin belleğinde tut (GC toplamasın)
            // Bellekte string veya byte[] olarak tutulacak:
            string keepAliveString = "0x" + Convert.ToHexString(testKeyBytes);
            byte[] keepAliveBytes = (byte[])testKeyBytes.Clone();

            // 5. Kendi sürecimizi (Environment.ProcessId) tara
            var progress = new Progress<(int Progress, string Status)>();
            string? foundKey = await RamAesExtractor.ScanProcessForAesKeyAsync(
                Environment.ProcessId,
                tempPak,
                progress);

            // 6. Doğrula: Bellekteki anahtarı yakaladı mı?
            Assert.NotNull(foundKey);
            Assert.Equal(expectedHexKey, foundKey, ignoreCase: true);

            GC.KeepAlive(keepAliveString);
            GC.KeepAlive(keepAliveBytes);
        }
        finally
        {
            if (File.Exists(tempPak))
            {
                try { File.Delete(tempPak); } catch { }
            }
        }
    }

    [Fact]
    public async Task LivePoppyPlaytimeRamScanTest()
    {
        var proc = Process.GetProcessesByName("Poppy_Playtime-Win64-Shipping").FirstOrDefault();
        if (proc == null) return;

        var pakPath = @"D:\SteamLibrary\steamapps\common\Poppy Playtime\WindowsNoEditor\Poppy_Playtime\Content\Paks\pakchunk0-WindowsNoEditor.pak";
        var progress = new Progress<(int Progress, string Status)>(p => Console.WriteLine($"[{p.Progress}%] {p.Status}"));

        string? foundKey = await RamAesExtractor.ScanProcessForAesKeyAsync(
            proc.Id,
            pakPath,
            progress);

        Console.WriteLine("FOUND KEY: " + foundKey);
        Assert.NotNull(foundKey);
    }
}

