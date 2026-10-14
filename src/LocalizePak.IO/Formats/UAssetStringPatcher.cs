using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LocalizePak.IO.Formats;

/// <summary>
/// .uasset veya .uexp içindeki taranan bir metin kaydını temsil eder.
/// </summary>
public sealed class UAssetStringEntry
{
    /// <summary>4 baytlık Little Endian uzunluk tam sayısının başladığı ofset.</summary>
    public int LengthOffset { get; set; }

    /// <summary>Karakter baytlarının başladığı ofset (LengthOffset + 4).</summary>
    public int ContentOffset => LengthOffset + 4;

    /// <summary>Dosyada kayıtlı olan int32 uzunluk değeri (pozitif ise UTF-8/ANSI bayt sayısı, negatif ise UTF-16 karakter sayısı).</summary>
    public int SerializedLength { get; set; }

    /// <summary>Orijinal dizenin metin içeriği.</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>Dizenin UTF-16LE mi yoksa UTF-8/ANSI mi olduğu.</summary>
    public bool IsUtf16 { get; set; }

    /// <summary>Uzunluk başlığı + dize baytları + null sonlandırıcı toplam bayt boyutu.</summary>
    public int TotalByteLength => 4 + (IsUtf16 ? Math.Abs(SerializedLength) * 2 : SerializedLength);
}

/// <summary>
/// Unreal Engine .uasset ve .uexp ikili dosyalarında gömülü FString/FText dizelerini tarayan,
/// çevrilen metin uzayıp kısaldığında Pascal Little Endian Length baytını yeniden hesaplayarak
/// ikili yapıyı bozmadan yamalayan endüstriyel hex dize yamalayıcısı.
/// </summary>
public static class UAssetStringPatcher
{
    public const uint UnrealPackageMagic = 0x9E2A83C1;

    /// <summary>
    /// Verilen bayt dizisinin geçerli bir Unreal paketi (.uasset) olup olmadığını kontrol eder.
    /// </summary>
    public static bool HasUnrealPackageMagic(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return false;
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4)) == UnrealPackageMagic;
    }

    /// <summary>
    /// Bir .uasset veya .uexp dosyasındaki tüm çevrilebilir FString dizelerini ayıklar.
    /// </summary>
    public static List<UAssetStringEntry> ExtractStrings(
        byte[] data,
        int minLength = 3,
        int maxLength = 8192)
    {
        var results = new List<UAssetStringEntry>();
        if (data == null || data.Length < 8) return results;

        var span = data.AsSpan();
        int maxScan = data.Length - 5;

        for (int i = 0; i <= maxScan; i++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(i, 4));

            // 1. Durum: UTF-8 / ANSI FString (length > 0)
            if (length > minLength && length <= maxLength)
            {
                int endOffset = i + 4 + length;
                if (endOffset <= data.Length)
                {
                    // Null sonlandırıcı kontrolü (son bayt 0 olmalıdır)
                    if (data[endOffset - 1] == 0)
                    {
                        var strBytes = span.Slice(i + 4, length - 1);
                        if (IsLikelyGameText(strBytes, isUtf16: false))
                        {
                            string text = Encoding.UTF8.GetString(strBytes);
                            results.Add(new UAssetStringEntry
                            {
                                LengthOffset = i,
                                SerializedLength = length,
                                OriginalText = text,
                                IsUtf16 = false
                            });

                            // Bir sonraki taramaya dize bitiminden devam et
                            i = endOffset - 1;
                            continue;
                        }
                    }
                }
            }
            // 2. Durum: UTF-16LE FString (length < 0)
            else if (length < -minLength && -length <= maxLength)
            {
                int charCount = -length;
                int byteCount = charCount * 2;
                int endOffset = i + 4 + byteCount;

                if (endOffset <= data.Length)
                {
                    // Null sonlandırıcı kontrolü (son 2 bayt 0x00, 0x00 olmalıdır)
                    if (data[endOffset - 2] == 0 && data[endOffset - 1] == 0)
                    {
                        var strBytes = span.Slice(i + 4, byteCount - 2);
                        if (IsLikelyGameText(strBytes, isUtf16: true))
                        {
                            string text = Encoding.Unicode.GetString(strBytes);
                            results.Add(new UAssetStringEntry
                            {
                                LengthOffset = i,
                                SerializedLength = length,
                                OriginalText = text,
                                IsUtf16 = true
                            });

                            i = endOffset - 1;
                            continue;
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Orijinal bayt dizisindeki dizeleri verilen çeviri sözlüğüne göre yamalar.
    /// Uzunluk baytlarını Little Endian olarak otomatik günceller ve ikili yapıyı korur.
    /// </summary>
    public static byte[] PatchStrings(
        byte[] originalData,
        IReadOnlyDictionary<string, string> translations,
        out int patchedCount)
    {
        patchedCount = 0;
        if (originalData == null || originalData.Length == 0 || translations == null || translations.Count == 0)
        {
            return originalData ?? Array.Empty<byte>();
        }

        var entries = ExtractStrings(originalData);
        if (entries.Count == 0)
        {
            return (byte[])originalData.Clone();
        }

        // Çakışmaları ve ofset kaymalarını önlemek için ofsete göre TERSTEN (sondan başa) sırala
        entries.Sort((a, b) => b.LengthOffset.CompareTo(a.LengthOffset));

        byte[] current = (byte[])originalData.Clone();

        foreach (var entry in entries)
        {
            if (translations.TryGetValue(entry.OriginalText, out var translated) &&
                !string.IsNullOrEmpty(translated) &&
                translated != entry.OriginalText)
            {
                byte[] replacementBytes;

                if (entry.IsUtf16)
                {
                    // UTF-16LE olarak yaz
                    byte[] payload = Encoding.Unicode.GetBytes(translated);
                    int charCount = (payload.Length / 2) + 1; // + null terminator
                    int newSerializedLength = -charCount;

                    replacementBytes = new byte[4 + payload.Length + 2];
                    BinaryPrimitives.WriteInt32LittleEndian(replacementBytes.AsSpan(0, 4), newSerializedLength);
                    payload.CopyTo(replacementBytes, 4);
                    replacementBytes[^2] = 0;
                    replacementBytes[^1] = 0;
                }
                else
                {
                    // Türkçe karakter içeriyorsa UTF-8 olarak kodla
                    byte[] payload = Encoding.UTF8.GetBytes(translated);
                    int newSerializedLength = payload.Length + 1; // + null terminator

                    replacementBytes = new byte[4 + payload.Length + 1];
                    BinaryPrimitives.WriteInt32LittleEndian(replacementBytes.AsSpan(0, 4), newSerializedLength);
                    payload.CopyTo(replacementBytes, 4);
                    replacementBytes[^1] = 0;
                }

                // Eski bloğu çıkartıp yeni bloğu yerleştir
                int oldLength = entry.TotalByteLength;
                int newLength = replacementBytes.Length;

                byte[] patched = new byte[current.Length - oldLength + newLength];

                // Önceki kısmı kopyala
                Array.Copy(current, 0, patched, 0, entry.LengthOffset);
                // Yeni bloğu kopyala
                Array.Copy(replacementBytes, 0, patched, entry.LengthOffset, newLength);
                // Sonraki kısmı kopyala
                int afterOffset = entry.LengthOffset + oldLength;
                int remaining = current.Length - afterOffset;
                if (remaining > 0)
                {
                    Array.Copy(current, afterOffset, patched, entry.LengthOffset + newLength, remaining);
                }

                current = patched;
                patchedCount++;
            }
        }

        return current;
    }

    /// <summary>
    /// Bir bayt akışının rastgele ikili veri yerine anlamlı bir oyun metni olup olmadığını doğrular.
    /// </summary>
    private static bool IsLikelyGameText(ReadOnlySpan<byte> bytes, bool isUtf16)
    {
        if (bytes.Length == 0) return false;

        string text;
        try
        {
            text = isUtf16
                ? Encoding.Unicode.GetString(bytes)
                : Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(text)) return false;

        int printableCount = 0;
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch) ||
                char.IsPunctuation(ch) ||
                char.IsWhiteSpace(ch) ||
                char.IsSymbol(ch))
            {
                printableCount++;
            }
        }

        // Metnin en az %85'i okunabilir karakter olmalıdır (ikili bytecode kırıntılarını elemek için)
        return ((double)printableCount / text.Length) >= 0.85;
    }
}
