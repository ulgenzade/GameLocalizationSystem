using System.Runtime.InteropServices;

namespace LocalizePak.IO.Helpers;

/// <summary>
/// Oodle sıkıştırma kütüphanesi için P/Invoke köprüsü.
/// Oodle'ı dinamik olarak yükler; bulunamazsa null döner.
/// </summary>
public static class OodleHelper
{
    private const string OodleDllName = "oo2core_9_win64.dll";
    private static nint _oodleHandle = nint.Zero;
    private static bool _initialized;
    private static OodleLZ_Decompress_Delegate? _decompressFunc;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long OodleLZ_Decompress_Delegate(
        byte[] compBuf, long compBufSize,
        byte[] rawBuf, long rawLen,
        int fuzzSafe, int checkCRC, int verbosity,
        nint decBufBase, long decBufSize,
        nint fpCallback, nint callbackUserData,
        nint decoderMemory, long decoderMemorySize,
        int threadPhase);

    private static readonly object _initLock = new();

    /// <summary>
    /// Oodle DLL'ini güvenli yollardan arar ve yükler.
    /// </summary>
    public static bool TryInitialize(string? searchPath = null)
    {
        lock (_initLock)
        {
            if (_initialized) return _decompressFunc is not null;
            _initialized = true;

            var candidates = new List<string>();
            // Güvenli arama: Önce uygulama dizini, sonra varsa açıkça belirtilen dizin
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OodleDllName));
            if (!string.IsNullOrWhiteSpace(searchPath))
                candidates.Add(Path.Combine(searchPath, OodleDllName));

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    _oodleHandle = NativeLibrary.Load(candidate);
                    if (_oodleHandle == nint.Zero) continue;

                    if (NativeLibrary.TryGetExport(_oodleHandle, "OodleLZ_Decompress", out var funcPtr))
                    {
                        _decompressFunc = Marshal.GetDelegateForFunctionPointer<OodleLZ_Decompress_Delegate>(funcPtr);
                        return true;
                    }
                }
                catch
                {
                    // Yükleme hatasında bir sonraki adayı dene
                }
            }

            return false;
        }
    }

    /// <summary>Oodle kütüphanesi yüklü mü?</summary>
    public static bool IsAvailable => _decompressFunc is not null;

    /// <summary>
    /// Oodle ile sıkıştırılmış veriyi açar.
    /// </summary>
    /// <returns>Açılmış veri. Oodle yüklü değilse veya boyut geçersizse null.</returns>
    public static byte[]? Decompress(byte[] compressed, long decompressedSize)
    {
        if (_decompressFunc is null) return null;
        if (decompressedSize <= 0 || decompressedSize > 1024L * 1024 * 1024) return null;

        var output = new byte[decompressedSize];
        var result = _decompressFunc(
            compressed, compressed.Length,
            output, decompressedSize,
            1, 0, 0,
            nint.Zero, 0,
            nint.Zero, nint.Zero,
            nint.Zero, 0, 3);

        return result > 0 ? output : null;
    }
}
