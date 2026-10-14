using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LocalizePak.IO.Formats;
using Xunit;

namespace LocalizePak.Tests;

public class UAssetStringPatcherTests
{
    private byte[] CreateUAssetBufferWithFStrings(params (string text, bool isUtf16)[] strings)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Dummy UAsset Header (Magic + summary dummy)
        bw.Write(UAssetStringPatcher.UnrealPackageMagic);
        bw.Write(new byte[32]); // Dummy metadata

        foreach (var (text, isUtf16) in strings)
        {
            if (isUtf16)
            {
                byte[] payload = Encoding.Unicode.GetBytes(text);
                int charCount = (payload.Length / 2) + 1;
                bw.Write(-charCount); // Negatif length for UTF-16
                bw.Write(payload);
                bw.Write((byte)0);
                bw.Write((byte)0);
            }
            else
            {
                byte[] payload = Encoding.UTF8.GetBytes(text);
                int byteCount = payload.Length + 1;
                bw.Write(byteCount); // Pozitif length for UTF-8
                bw.Write(payload);
                bw.Write((byte)0);
            }

            // Araya biraz kukla ikili veri koy (property id, opcode vb.)
            bw.Write(new byte[16]);
        }

        return ms.ToArray();
    }

    [Fact]
    public void HasUnrealPackageMagic_ValidHeader_ReturnsTrue()
    {
        byte[] buffer = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), UAssetStringPatcher.UnrealPackageMagic);
        Assert.True(UAssetStringPatcher.HasUnrealPackageMagic(buffer));
    }

    [Fact]
    public void ExtractStrings_FindsUtf8AndUtf16Strings()
    {
        byte[] buffer = CreateUAssetBufferWithFStrings(
            ("Press [E] to Interact", false),
            ("Quest Completed!", true)
        );

        var extracted = UAssetStringPatcher.ExtractStrings(buffer);

        Assert.Equal(2, extracted.Count);
        Assert.Equal("Press [E] to Interact", extracted[0].OriginalText);
        Assert.False(extracted[0].IsUtf16);

        Assert.Equal("Quest Completed!", extracted[1].OriginalText);
        Assert.True(extracted[1].IsUtf16);
    }

    [Fact]
    public void PatchStrings_ExpandsLengthAndUpdatesLittleEndianLengthByte()
    {
        // "Attack" (6 karakter) -> "Şiddetli Saldırı" (16 karakter, UTF-8'de 19 bayt)
        byte[] buffer = CreateUAssetBufferWithFStrings(
            ("Attack", false),
            ("Defense", false)
        );

        var translations = new Dictionary<string, string>
        {
            ["Attack"] = "Şiddetli Saldırı",
            ["Defense"] = "Savunma"
        };

        byte[] patched = UAssetStringPatcher.PatchStrings(buffer, translations, out int patchedCount);

        Assert.Equal(2, patchedCount);

        var reExtracted = UAssetStringPatcher.ExtractStrings(patched);
        Assert.Equal(2, reExtracted.Count);
        Assert.Equal("Şiddetli Saldırı", reExtracted[0].OriginalText);
        Assert.Equal("Savunma", reExtracted[1].OriginalText);

        // Uzunluk baytının doğru hesaplandığını doğrula (UTF-8 bayt sayısı + 1)
        int expectedLen = Encoding.UTF8.GetByteCount("Şiddetli Saldırı") + 1;
        Assert.Equal(expectedLen, reExtracted[0].SerializedLength);
    }

    [Fact]
    public void PatchStrings_Utf16Expansion_PreservesNegativeLengthByte()
    {
        byte[] buffer = CreateUAssetBufferWithFStrings(
            ("Save Game", true)
        );

        var translations = new Dictionary<string, string>
        {
            ["Save Game"] = "Oyunu Kaydet ve Çık"
        };

        byte[] patched = UAssetStringPatcher.PatchStrings(buffer, translations, out int patchedCount);

        Assert.Equal(1, patchedCount);

        var reExtracted = UAssetStringPatcher.ExtractStrings(patched);
        Assert.Single(reExtracted);
        Assert.Equal("Oyunu Kaydet ve Çık", reExtracted[0].OriginalText);
        Assert.True(reExtracted[0].IsUtf16);

        // UTF-16 serialized length = -(charCount + 1)
        int expectedChars = "Oyunu Kaydet ve Çık".Length + 1;
        Assert.Equal(-expectedChars, reExtracted[0].SerializedLength);
    }
}
