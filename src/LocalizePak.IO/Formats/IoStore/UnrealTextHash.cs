using System.Buffers.Binary;
using System.IO.Hashing;

namespace LocalizePak.IO.Formats.IoStore;

/// <summary>
/// Unreal Engine metin hash'leri. .locres girdisindeki <c>SourceStringHash</c> alanı <c>FCrc::StrCrc32</c> ile hesaplanır;
/// motor, FText'in kaynak metninin hash'i bu değerle eşleşmezse çeviriyi "eski" sayıp göstermez.
/// </summary>
public static class UnrealTextHash
{
    /// <summary>
    /// <c>FCrc::StrCrc32</c>: her UTF-16 karakteri 4 bayt (küçük uçlu, üst baytlar sıfır) olarak standart CRC-32'den geçirir.
    /// </summary>
    public static uint StrCrc32(string text)
    {
        var crc = new Crc32();
        Span<byte> buffer = stackalloc byte[1024];
        int filled = 0;
        foreach (char c in text)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(filled, 4), c);
            filled += 4;
            if (filled == buffer.Length)
            {
                crc.Append(buffer);
                filled = 0;
            }
        }
        if (filled > 0) crc.Append(buffer[..filled]);
        return crc.GetCurrentHashAsUInt32();
    }
}
