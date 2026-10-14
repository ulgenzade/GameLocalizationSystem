using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats;

namespace LocalizePak.IO;

/// <summary>
/// Unreal Engine .locres dosyalarını okuyup yazan servis. Asıl ikili mantık <see cref="LocresCodec"/> içindedir
/// (Legacy, Compact, Optimized_CRC32 ve Optimized_CityHash64_UTF16 sürümleri; hash değerleri korunur).
/// </summary>
public sealed class LocresService : ILocresSerializer
{
    /// <inheritdoc/>
    public async Task<LocresFile> DeserializeAsync(Stream stream, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return LocresCodec.Read(ms.ToArray());
    }

    /// <inheritdoc/>
    public Task<byte[]> SerializeAsync(LocresFile locresFile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(LocresCodec.Write(locresFile));
    }
}
