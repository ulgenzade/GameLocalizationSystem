using System.Security.Cryptography;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Bir AES-256 anahtar adayının, Unreal .pak dosyasının şifreli indeksini çözüp çözmediğini hızlıca sınar.
/// İndeks, mount point FString'i ("../../../" gibi) ile başlar; ilk 16 baytlık blok AES-ECB ile çözülüp
/// uzunluk ve yazdırılabilir ASCII karakterler kontrol edilir. Örnek başına iş parçacığı güvenli değildir;
/// paralel taramada her iş parçacığı kendi örneğini oluşturmalıdır.
/// </summary>
public sealed class PakIndexKeyTester : IDisposable
{
    private readonly byte[] _cipherBlock;
    private readonly Aes _aes;
    private readonly byte[] _key = new byte[32];
    private readonly byte[] _plain = new byte[16];

    public PakIndexKeyTester(byte[] encryptedIndexHead)
    {
        if (encryptedIndexHead.Length < 16)
            throw new ArgumentException("En az 16 bayt şifreli indeks verisi gerekir.", nameof(encryptedIndexHead));

        _cipherBlock = encryptedIndexHead[..16];
        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
    }

    /// <summary>32 baytlık anahtar adayını sınar.</summary>
    public bool Test(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32) return false;
        key.CopyTo(_key);
        try { _aes.Key = _key; } catch (CryptographicException) { return false; }

        _aes.DecryptEcb(_cipherBlock, _plain, PaddingMode.None);

        int mountLen = BitConverter.ToInt32(_plain, 0);
        if (mountLen < 2 || mountLen > 1024) return false;

        int check = Math.Min(mountLen - 1, 12);
        if (check < 2) return false;
        for (int i = 0; i < check; i++)
        {
            byte b = _plain[4 + i];
            if (b < 32 || b > 126) return false;
        }
        return true;
    }

    public void Dispose() => _aes.Dispose();

    /// <summary>
    /// Pak dosyasının footer'ından şifreli indeksin ilk 16 baytını okur.
    /// İndeks şifreli değilse veya footer bulunamazsa null döner.
    /// </summary>
    public static byte[]? ReadEncryptedIndexHead(string pakFilePath)
    {
        using var fs = new FileStream(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length < 64) return null;

        int scanSize = (int)Math.Min(fs.Length, 8192);
        var tail = new byte[scanSize];
        fs.Seek(-scanSize, SeekOrigin.End);
        fs.ReadExactly(tail, 0, scanSize);

        for (int i = scanSize - 4; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) != 0x5A6F12E1) continue;
            if (i + 24 > scanSize) continue;

            int version = BitConverter.ToInt32(tail, i + 4);
            long indexOffset = BitConverter.ToInt64(tail, i + 8);
            if (indexOffset <= 0 || indexOffset + 16 > fs.Length) continue;

            // v6+ footer'ında magic'ten önceki bayt "indeks şifreli" bayrağıdır.
            if (version >= 6 && i >= 1 && tail[i - 1] == 0) return null;

            var head = new byte[16];
            fs.Seek(indexOffset, SeekOrigin.Begin);
            fs.ReadExactly(head, 0, head.Length);
            return head;
        }

        return null;
    }
}
