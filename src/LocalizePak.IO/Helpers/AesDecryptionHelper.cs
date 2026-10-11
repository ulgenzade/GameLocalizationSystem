using System.Security.Cryptography;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Bellek içi AES-256-CBC şifre çözme yardımcı sınıfı.
/// </summary>
public static class AesDecryptionHelper
{
    private const int KeySizeBytes = 32; // 256-bit

    /// <summary>
    /// Hex formatındaki AES anahtarını doğrular.
    /// </summary>
    /// <param name="hexKey">0x önekli veya onsuz 64 hex karakter.</param>
    public static bool ValidateKey(string hexKey, out byte[] keyBytes)
    {
        keyBytes = [];
        if (string.IsNullOrWhiteSpace(hexKey)) return false;

        // 0x veya 0X önekini kaldır
        var hex = hexKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hexKey[2..]
            : hexKey;

        if (hex.Length != KeySizeBytes * 2) return false;

        try
        {
            keyBytes = Convert.FromHexString(hex);
            return keyBytes.Length == KeySizeBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// AES-256-CBC ile şifrelenmiş veriyi çözer.
    /// .pak dosyalarında IV genellikle ilk 16 byte veya footer'dan gelir.
    /// </summary>
    /// <param name="encryptedData">Şifreli veri bloğu.</param>
    /// <param name="key">256-bit AES anahtarı.</param>
    /// <param name="iv">16-byte IV (null ise sıfır IV kullanılır).</param>
    public static byte[] Decrypt(byte[] encryptedData, byte[] key, byte[]? iv = null)
    {
        iv ??= new byte[16]; // Sıfır IV (bazı oyunlar kullanır)

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.Zeros; // .pak formatı PKCS7 yerine Zero padding kullanır

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(encryptedData);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var result = new MemoryStream();
        cs.CopyTo(result);
        return result.ToArray();
    }

    /// <summary>
    /// Akış tabanlı AES şifre çözme.
    /// </summary>
    public static Stream DecryptStream(Stream encryptedStream, byte[] key, byte[]? iv = null)
    {
        iv ??= new byte[16];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.Zeros;

        using var decryptor = aes.CreateDecryptor();
        using var cs = new CryptoStream(encryptedStream, decryptor, CryptoStreamMode.Read, leaveOpen: true);
        var result = new MemoryStream();
        cs.CopyTo(result);
        result.Position = 0;
        return result;
    }

    /// <summary>
    /// AES-256-CBC ile veriyi şifreler (Unreal Engine &amp; Godot standartlarına uygun Zero padding).
    /// </summary>
    public static byte[] Encrypt(byte[] data, byte[] key, byte[]? iv = null)
    {
        iv ??= new byte[16];

        int remainder = data.Length % 16;
        int paddedLength = remainder == 0 ? data.Length : data.Length + (16 - remainder);
        byte[] buffer = new byte[paddedLength];
        Array.Copy(data, buffer, data.Length);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(buffer, 0, buffer.Length);
    }
}
