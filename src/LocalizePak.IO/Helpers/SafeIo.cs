using System.Text;
using LocalizePak.Core.Models;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Güvenilmeyen arşiv verisiyle çalışırken ortak güvenlik korumaları:
/// yol kaçışı (zip-slip), girdi boyutu sınırları, sıkıştırma bombası koruması ve .bat metin kaçışı.
/// </summary>
public static class SafeIo
{
    /// <summary>Tek bir arşiv girdisi için izin verilen azami boyut (proje standardı: 1 GB).</summary>
    public const long MaxEntrySize = 1L * 1024 * 1024 * 1024;

    /// <summary>
    /// Arşiv içi sanal yolu <paramref name="rootDirectory"/> altında güvenli bir tam yola çevirir.
    /// "../", mutlak yol veya sürücü harfi ile kök dışına çıkmaya çalışan yollar reddedilir.
    /// </summary>
    /// <exception cref="InvalidDataException">Yol kök dizinin dışına çıkıyorsa.</exception>
    public static string ResolveChildPath(string rootDirectory, string virtualPath)
    {
        var relative = virtualPath.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException($"Güvensiz arşiv yolu reddedildi: '{virtualPath}'");

        var root = Path.GetFullPath(rootDirectory);
        if (!Path.EndsInDirectorySeparator(root))
            root += Path.DirectorySeparatorChar;

        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
            throw new InvalidDataException($"Güvensiz arşiv yolu reddedildi (kök dışına çıkıyor): '{virtualPath}'");

        return full;
    }

    /// <summary>
    /// Ofset tabanlı okunan bir girdinin boyut ve konumunun dosya sınırları içinde olduğunu doğrular.
    /// Bozuk veya kötü niyetli bir dizin tablosunun dev bellek ayırmasını / dosya dışı okumayı önler.
    /// </summary>
    public static void ValidateEntryRange(PakEntry entry, long archiveLength, bool checkUncompressedOnly = false)
    {
        long readSize = checkUncompressedOnly ? entry.UncompressedSize : entry.CompressedSize;
        if (entry.DataOffset < 0 || readSize < 0 || entry.UncompressedSize < 0 ||
            readSize > MaxEntrySize || entry.UncompressedSize > MaxEntrySize ||
            entry.DataOffset + readSize > archiveLength)
        {
            throw new InvalidDataException(
                $"'{entry.VirtualPath}' girdisinin boyutu/konumu geçersiz (ofset {entry.DataOffset}, boyut {readSize}, arşiv {archiveLength} bayt).");
        }
    }

    /// <summary>
    /// <paramref name="source"/> akışını en fazla <paramref name="maxBytes"/> bayt olacak şekilde kopyalar.
    /// Sınır aşılırsa sıkıştırma bombası olarak kabul edilip <see cref="InvalidDataException"/> fırlatılır.
    /// </summary>
    public static void CopyBounded(Stream source, Stream destination, long maxBytes)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new DecompressionLimitExceededException(maxBytes);
            destination.Write(buffer, 0, read);
        }
    }

    /// <inheritdoc cref="CopyBounded"/>
    public static async Task CopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct = default)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new DecompressionLimitExceededException(maxBytes);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Açılmış veri için makul üst sınır: bildirilen boyut + %10 pay (en az 1 MB), ama asla <see cref="MaxEntrySize"/> üstü değil.
    /// Bildirilen boyut bilinmiyorsa (≤ 0) doğrudan <see cref="MaxEntrySize"/>.
    /// </summary>
    public static long DecompressionLimit(long declaredUncompressedSize)
    {
        if (declaredUncompressedSize <= 0) return MaxEntrySize;
        long margin = Math.Max(1024 * 1024, declaredUncompressedSize / 10);
        return Math.Min(MaxEntrySize, declaredUncompressedSize + margin);
    }

    /// <summary>
    /// Windows .bat betiğine gömülecek metni güvenli hale getirir: komut ayırıcı ve değişken
    /// genişletme karakterleri (&amp; | &lt; &gt; ^ % ! ") ve satır sonları çıkarılır.
    /// Böylece oyun/dosya adı üzerinden komut enjeksiyonu yapılamaz.
    /// </summary>
    public static string SanitizeForBatch(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '&' or '|' or '<' or '>' or '^' or '%' or '!' or '"' or '\r' or '\n' or '\0')
                continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}

/// <summary>Açılan veri izin verilen boyutu aştığında (sıkıştırma bombası) fırlatılır.</summary>
public sealed class DecompressionLimitExceededException(long maxBytes)
    : IOException($"Açılan veri izin verilen boyutu aşıyor ({maxBytes:N0} bayt) — bozuk veya kötü niyetli arşiv.");
