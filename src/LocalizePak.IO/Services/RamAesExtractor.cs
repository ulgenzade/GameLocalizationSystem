using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalizePak.IO.Helpers;

namespace LocalizePak.IO.Services;

/// <summary>
/// Çalışan oyun süreci bilgisi.
/// </summary>
public record RunningGameProcess(
    int Id,
    string ProcessName,
    string MainWindowTitle,
    string FilePath);

/// <summary>
/// Oyunun çalışan RAM belleğini (ReadProcessMemory) yüksek performanslı ve aşamalı
/// (Modül -> String -> Heap) olarak tarayarak 256-bit (32 bayt) AES anahtarını yakalayan servis.
/// </summary>
public static class RamAesExtractor
{
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_NOACCESS = 0x01;
    private const uint PAGE_GUARD = 0x100;

    private static bool IsReadableMemory(uint protect)
    {
        if ((protect & PAGE_NOACCESS) != 0) return false;
        if ((protect & PAGE_GUARD) != 0) return false;
        // PAGE_READONLY, PAGE_READWRITE, PAGE_WRITECOPY, PAGE_EXECUTE_READ, PAGE_EXECUTE_READWRITE, PAGE_EXECUTE_WRITECOPY
        return (protect & 0xEE) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const string SE_DEBUG_NAME = "SeDebugPrivilege";
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    /// <summary>
    /// Windows işletim sisteminde yönetici ayrıcalıklarını (SeDebugPrivilege) etkinleştirerek
    /// her türlü oyun sürecinin RAM belleğinin engelsizce okunabilmesini sağlar.
    /// </summary>
    public static void EnableSeDebugPrivilege()
    {
        try
        {
            if (OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                if (LookupPrivilegeValue(null, SE_DEBUG_NAME, out LUID luid))
                {
                    TOKEN_PRIVILEGES tp = new()
                    {
                        PrivilegeCount = 1,
                        Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                    };
                    AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
                CloseHandle(hToken);
            }
        }
        catch { }
    }

    /// <summary>
    /// Bilgisayarda o an çalışan pencere sahibi kullanıcı süreçlerini (oyunları) listeler.
    /// Tüm Process tanıtıcıları sızıntıyı önlemek için güvenle serbest bırakılır.
    /// </summary>
    public static List<RunningGameProcess> GetRunningProcesses()
    {
        var list = new List<RunningGameProcess>();
        var currentPid = Environment.ProcessId;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return list;
        }

        foreach (var p in processes)
        {
            using (p)
            {
                try
                {
                    if (p.Id == currentPid || p.Id <= 4) continue;
                    if (string.IsNullOrWhiteSpace(p.MainWindowTitle) && string.IsNullOrWhiteSpace(p.ProcessName)) continue;

                    // Sistem süreçlerini filtrele
                    var name = p.ProcessName.ToLowerInvariant();
                    if (name is "explorer" or "system" or "svchost" or "devenv" or "powershell" or "cmd" or "gls")
                        continue;

                    string path = string.Empty;
                    try { path = p.MainModule?.FileName ?? string.Empty; } catch { }

                    list.Add(new RunningGameProcess(p.Id, p.ProcessName, p.MainWindowTitle, path));
                }
                catch
                {
                    // İzin verilmeyen süreçleri atla
                }
            }
        }

        return list.OrderByDescending(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                   .ThenBy(p => p.ProcessName)
                   .ToList();
    }

    /// <summary>
    /// Seçili sürecin belleğini aşamalı (MainModule -> ASCII/UTF16 -> Heap) tarayarak
    /// 256-bit AES anahtarını tespit eder ve .pak başlığıyla doğrular.
    /// </summary>
    public static async Task<string?> ScanProcessForAesKeyAsync(
        int processId,
        string? pakFilePath = null,
        IProgress<(int Progress, string Status)>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            EnableSeDebugPrivilege();

            IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
            if (hProcess == IntPtr.Zero)
                throw new UnauthorizedAccessException("Oyun sürecine erişilemedi. Lütfen GLS'yi Yönetici Olarak (Run as Administrator) çalıştırmayı deneyin.");

            // Eğer kullanıcı paket seçmediyse oyunun Content/Paks klasöründen otomatik algıla
            if (string.IsNullOrEmpty(pakFilePath) || !File.Exists(pakFilePath))
            {
                pakFilePath = TryFindGamePakFile(processId);
                if (!string.IsNullOrEmpty(pakFilePath))
                {
                    progress?.Report((2, $"Oyun paketi otomatik algılandı: {Path.GetFileName(pakFilePath)}"));
                }
            }

            byte[]? pakTestBuffer = null;
            if (!string.IsNullOrEmpty(pakFilePath) && File.Exists(pakFilePath))
            {
                try
                {
                    pakTestBuffer = ReadEncryptedIndexHead(pakFilePath);
                }
                catch { }
            }

            try
            {
                using var aesEcb = Aes.Create();
                aesEcb.Mode = CipherMode.ECB;
                aesEcb.Padding = PaddingMode.None;

                // ─────────────────────────────────────────────────────────────
                // AŞAMA 1: Oyunun Ana Modülünü (MainModule) Tara
                // Unreal Engine oyunlarında AES anahtarı %90 ihtimalle ana exe'nin .data / .rdata alanındadır.
                // ─────────────────────────────────────────────────────────────
                progress?.Report((5, "Aşama 1/4: Oyunun ana modülü (EXE) taranıyor..."));

                string? key = ScanMainModule(hProcess, processId, aesEcb, pakTestBuffer, ct);
                if (!string.IsNullOrEmpty(key))
                {
                    progress?.Report((100, "🎉 AES anahtarı oyunun ana modülünden yakalandı!"));
                    return key;
                }

                // ─────────────────────────────────────────────────────────────
                // AŞAMA 2: Hex / ASCII / UTF-16 String Formatında Anahtar Arama ("0x...")
                // DefaultCrypto.ini'den derlenen stringler bellekte doğrudan metin olarak durur.
                // ─────────────────────────────────────────────────────────────
                progress?.Report((25, "Aşama 2/4: Bellekteki string ve config tabloları taranıyor..."));

                key = ScanStringPatterns(hProcess, aesEcb, pakTestBuffer, ct);
                if (!string.IsNullOrEmpty(key))
                {
                    progress?.Report((100, "🎉 AES anahtarı yapılandırma tablosundan yakalandı!"));
                    return key;
                }

                // ─────────────────────────────────────────────────────────────
                // AŞAMA 3: Oyun Klasöründeki Yüklü DLL Modüllerini Tara
                // ─────────────────────────────────────────────────────────────
                progress?.Report((45, "Aşama 3/4: Oyun DLL modülleri taranıyor..."));

                key = ScanGameDlls(hProcess, processId, aesEcb, pakTestBuffer, ct);
                if (!string.IsNullOrEmpty(key))
                {
                    progress?.Report((100, "🎉 AES anahtarı oyun DLL'lerinden yakalandı!"));
                    return key;
                }

                // ─────────────────────────────────────────────────────────────
                // AŞAMA 4: Dinamik Heap / RAM Taraması (Akıllı Filtreli & Hızlı)
                // 32 MB'tan büyük doku/render alanları taranmaz; yalnızca nesne belleği incelenir.
                // ─────────────────────────────────────────────────────────────
                progress?.Report((60, "Aşama 4/4: Dinamik nesne belleği (Heap) taranıyor..."));

                key = ScanHeapMemory(hProcess, aesEcb, pakTestBuffer, progress, ct);
                if (!string.IsNullOrEmpty(key))
                {
                    progress?.Report((100, "🎉 AES anahtarı oyun belleğinden başarıyla çıkarıldı!"));
                    return key;
                }

                return null;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }, ct);
    }

    /// <summary>
    /// Eski IProgress&lt;int&gt; uyumluluğu için aşırı yükleme (overload).
    /// </summary>
    public static async Task<string?> ScanProcessForAesKeyAsync(
        int processId,
        string? pakFilePath,
        IProgress<int>? progress,
        CancellationToken ct = default)
    {
        var wrappedProgress = progress != null
            ? new Progress<(int Progress, string Status)>(p => progress.Report(p.Progress))
            : null;

        return await ScanProcessForAesKeyAsync(processId, pakFilePath, wrappedProgress, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Aşama 1: Oyun Ana Modülü Taraması
    // ─────────────────────────────────────────────────────────────────────────
    private static string? ScanMainModule(
        IntPtr hProcess,
        int processId,
        Aes aesEcb,
        byte[]? pakTestBuffer,
        CancellationToken ct)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            var mainModule = proc.MainModule;
            if (mainModule == null) return null;

            long baseAddr = mainModule.BaseAddress.ToInt64();
            long moduleSize = mainModule.ModuleMemorySize;
            long endAddr = baseAddr + moduleSize;

            return ScanAddressRange(hProcess, baseAddr, endAddr, aesEcb, pakTestBuffer, ct);
        }
        catch
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Aşama 2: String Formatında Arama ("0x" + 64 Hex Karakter, ASCII / UTF-16)
    // ─────────────────────────────────────────────────────────────────────────
    private static string? ScanStringPatterns(
        IntPtr hProcess,
        Aes aesEcb,
        byte[]? pakTestBuffer,
        CancellationToken ct)
    {
        int memInfoSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        IntPtr address = IntPtr.Zero;
        long maxAddress = 0x7FFFFFFFFFFF;
        byte[] buffer = new byte[256 * 1024];

        while (address.ToInt64() < maxAddress)
        {
            ct.ThrowIfCancellationRequested();

            if (VirtualQueryEx(hProcess, address, out var memInfo, (uint)memInfoSize) == 0)
                break;

            bool isValid = memInfo.State == MEM_COMMIT &&
                           IsReadableMemory(memInfo.Protect) &&
                           memInfo.RegionSize.ToInt64() > 0 &&
                           memInfo.RegionSize.ToInt64() <= 32 * 1024 * 1024; // Yalnızca küçük-orta bölgeler

            if (isValid)
            {
                long regionSize = memInfo.RegionSize.ToInt64();
                long readOffset = 0;

                while (readOffset < regionSize)
                {
                    int bytesToRead = (int)Math.Min(buffer.Length, regionSize - readOffset);
                    IntPtr readAddr = new IntPtr(memInfo.BaseAddress.ToInt64() + readOffset);

                    if (ReadProcessMemory(hProcess, readAddr, buffer, bytesToRead, out var bytesRead) && bytesRead.ToInt64() >= 66)
                    {
                        int count = (int)bytesRead.ToInt64();

                        // 1. ASCII "0x" veya "0X" araması
                        for (int i = 0; i <= count - 66; i++)
                        {
                            if (buffer[i] == (byte)'0' && (buffer[i + 1] == (byte)'x' || buffer[i + 1] == (byte)'X'))
                            {
                                if (IsHexChars(buffer.AsSpan(i + 2, 64)))
                                {
                                    var hexKey = "0x" + Encoding.ASCII.GetString(buffer, i + 2, 64);
                                    if (ValidateAndTestKey(hexKey, aesEcb, pakTestBuffer))
                                        return hexKey;
                                }
                            }
                        }

                        // 2. UTF-16 "0\0x\0" araması
                        for (int i = 0; i <= count - 132; i += 2)
                        {
                            if (buffer[i] == (byte)'0' && buffer[i + 1] == 0 &&
                                (buffer[i + 2] == (byte)'x' || buffer[i + 2] == (byte)'X') && buffer[i + 3] == 0)
                            {
                                if (IsHexCharsUtf16(buffer.AsSpan(i + 4, 128)))
                                {
                                    var hexKey = "0x" + Encoding.Unicode.GetString(buffer, i + 4, 128);
                                    if (ValidateAndTestKey(hexKey, aesEcb, pakTestBuffer))
                                        return hexKey;
                                }
                            }
                        }
                    }

                    readOffset += bytesToRead;
                }
            }

            address = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Aşama 3: Oyun Klasöründeki Yüklü DLL Modüllerini Tara
    // ─────────────────────────────────────────────────────────────────────────
    private static string? ScanGameDlls(
        IntPtr hProcess,
        int processId,
        Aes aesEcb,
        byte[]? pakTestBuffer,
        CancellationToken ct)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            var mainModulePath = proc.MainModule?.FileName;
            var gameDir = !string.IsNullOrEmpty(mainModulePath) ? Path.GetDirectoryName(mainModulePath) : null;

            if (string.IsNullOrEmpty(gameDir)) return null;

            foreach (ProcessModule mod in proc.Modules)
            {
                ct.ThrowIfCancellationRequested();
                if (mod.FileName == null || mod == proc.MainModule) continue;

                // Yalnızca oyun klasörü içindeki DLL'leri tara (Windows sistem DLL'lerini atla)
                if (mod.FileName.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase))
                {
                    long baseAddr = mod.BaseAddress.ToInt64();
                    long moduleSize = mod.ModuleMemorySize;
                    var key = ScanAddressRange(hProcess, baseAddr, baseAddr + moduleSize, aesEcb, pakTestBuffer, ct);
                    if (!string.IsNullOrEmpty(key)) return key;
                }
            }
        }
        catch { }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Aşama 4: Dinamik Heap / RAM Taraması (Akıllı Boyut Filtreli)
    // ─────────────────────────────────────────────────────────────────────────
    private static string? ScanHeapMemory(
        IntPtr hProcess,
        Aes aesEcb,
        byte[]? pakTestBuffer,
        IProgress<(int Progress, string Status)>? progress,
        CancellationToken ct)
    {
        int memInfoSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        IntPtr address = IntPtr.Zero;
        long maxAddress = 0x7FFFFFFFFFFF;

        // Taranabilir bölgeleri listele ve toplam boyutu hesapla
        var regions = new List<(IntPtr BaseAddress, long Size)>();
        long totalCommitBytes = 0;

        while (address.ToInt64() < maxAddress)
        {
            ct.ThrowIfCancellationRequested();

            if (VirtualQueryEx(hProcess, address, out var memInfo, (uint)memInfoSize) == 0)
                break;

            bool isValid = memInfo.State == MEM_COMMIT &&
                           IsReadableMemory(memInfo.Protect) &&
                           memInfo.RegionSize.ToInt64() > 0 &&
                           memInfo.RegionSize.ToInt64() <= 128 * 1024 * 1024; // UE4 & UE5 Large Page Allocator havuzlarını kapsar (<=128 MB)

            if (isValid)
            {
                regions.Add((memInfo.BaseAddress, memInfo.RegionSize.ToInt64()));
                totalCommitBytes += memInfo.RegionSize.ToInt64();
            }

            address = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
        }

        if (regions.Count == 0) return null;

        byte[] chunkBuffer = new byte[512 * 1024]; // 512 KB parçalar ile syscall sayısını azalt
        long scannedBytes = 0;

        foreach (var (baseAddr, regionSize) in regions)
        {
            ct.ThrowIfCancellationRequested();
            long readOffset = 0;

            while (readOffset < regionSize)
            {
                int bytesToRead = (int)Math.Min(chunkBuffer.Length, regionSize - readOffset);
                IntPtr readAddr = new IntPtr(baseAddr.ToInt64() + readOffset);

                if (ReadProcessMemory(hProcess, readAddr, chunkBuffer, bytesToRead, out var bytesRead) && bytesRead.ToInt64() >= 32)
                {
                    int readCount = (int)bytesRead.ToInt64();
                    var foundKey = ScanBufferFast(chunkBuffer.AsSpan(0, readCount), aesEcb, pakTestBuffer);
                    if (!string.IsNullOrEmpty(foundKey))
                    {
                        return foundKey;
                    }
                }

                readOffset += bytesToRead;
                scannedBytes += bytesToRead;
            }

            if (totalCommitBytes > 0)
            {
                int percent = 60 + (int)((scannedBytes / (double)totalCommitBytes) * 39.0);
                progress?.Report((percent, $"RAM nesneleri taranıyor: %{percent}"));
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Yardımcı Bellek Arama Fonksiyonları
    // ─────────────────────────────────────────────────────────────────────────

    private static string? ScanAddressRange(
        IntPtr hProcess,
        long startAddr,
        long endAddr,
        Aes aesEcb,
        byte[]? pakTestBuffer,
        CancellationToken ct)
    {
        int memInfoSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        IntPtr address = new IntPtr(startAddr);
        byte[] chunkBuffer = new byte[512 * 1024];

        while (address.ToInt64() < endAddr)
        {
            ct.ThrowIfCancellationRequested();

            if (VirtualQueryEx(hProcess, address, out var memInfo, (uint)memInfoSize) == 0)
                break;

            bool isValid = memInfo.State == MEM_COMMIT &&
                           IsReadableMemory(memInfo.Protect) &&
                           memInfo.RegionSize.ToInt64() > 0;

            if (isValid)
            {
                long regionEnd = Math.Min(endAddr, memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
                long regionSize = regionEnd - memInfo.BaseAddress.ToInt64();
                long readOffset = 0;

                while (readOffset < regionSize)
                {
                    int bytesToRead = (int)Math.Min(chunkBuffer.Length, regionSize - readOffset);
                    IntPtr readAddr = new IntPtr(memInfo.BaseAddress.ToInt64() + readOffset);

                    if (ReadProcessMemory(hProcess, readAddr, chunkBuffer, bytesToRead, out var bytesRead) && bytesRead.ToInt64() >= 32)
                    {
                        int readCount = (int)bytesRead.ToInt64();
                        var foundKey = ScanBufferFast(chunkBuffer.AsSpan(0, readCount), aesEcb, pakTestBuffer);
                        if (!string.IsNullOrEmpty(foundKey))
                        {
                            return foundKey;
                        }
                    }

                    readOffset += bytesToRead;
                }
            }

            address = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
        }

        return null;
    }

    /// <summary>
    /// Verilen bayt tamponundaki 32 baytlık AES adaylarını sıfır tahsis (zero allocation) ile test eder.
    /// Her bayt konumunu (1-bayt adımla) eksiksiz tarar.
    /// </summary>
    private static string? ScanBufferFast(
        ReadOnlySpan<byte> buffer,
        Aes aesEcb,
        byte[]? pakTestBuffer)
    {
        int length = buffer.Length;
        Span<byte> decrypted = stackalloc byte[32];

        for (int i = 0; i <= length - 32; i++)
        {
            var candidateSpan = buffer.Slice(i, 32);

            // 1. Hızlı Entropi & Null Filtresi: Şifreleme anahtarları rastgeledir, nadiren sıfır içerir
            int zeroCount = 0;
            for (int j = 0; j < 32; j++)
            {
                if (candidateSpan[j] == 0) zeroCount++;
            }
            if (zeroCount > 3) continue; // 3'ten fazla null bayt varsa AES anahtarı değildir

            // 2. 64-bit Pointer Deseni Filtresi: Ardışık bellek adreslerini ele (örn. 0x00007FF...)
            if (candidateSpan[6] == 0x7F || (candidateSpan[7] == 0x00 && candidateSpan[6] == 0x00))
            {
                if (candidateSpan[14] == 0x7F || candidateSpan[22] == 0x7F || candidateSpan[30] == 0x7F)
                    continue;
            }

            // 3. Tekrar Eden Blok Filtresi (örn: float dizileri, int dizileri)
            if (candidateSpan[0] == candidateSpan[4] && candidateSpan[4] == candidateSpan[8] && candidateSpan[8] == candidateSpan[12])
                continue;

            // 4. Okunabilir ASCII Metin Filtresi (DOS stub, telif hakları, İngilizce cümleler şifre olamaz)
            int asciiPrintable = 0;
            for (int j = 0; j < 32; j++)
            {
                byte b = candidateSpan[j];
                if (b >= 32 && b <= 126) asciiPrintable++;
            }
            if (asciiPrintable > 18) continue; // 32 baytın yarısından fazlası ASCII metinse elenir

            // 5. .pak İndeks Başlığı ile Kesin Doğrulama
            if (pakTestBuffer != null)
            {
                if (TestCandidateKey(aesEcb, pakTestBuffer, candidateSpan, decrypted))
                {
                    return "0x" + Convert.ToHexString(candidateSpan);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Aday 32-bayt anahtarı .pak index başlığı üzerinde AES-ECB ile deşifre ederek MountPoint doğrulaması yapar.
    /// Hem ASCII hem de Unreal Engine FString UTF-16 (mountLen < 0) formatlarını destekler.
    /// </summary>
    private static bool TestCandidateKey(
        Aes aesEcb,
        byte[] encryptedIndexHead,
        ReadOnlySpan<byte> candidateKey,
        Span<byte> decryptedOut)
    {
        try
        {
            aesEcb.Key = candidateKey.ToArray();
            int written = aesEcb.DecryptEcb(encryptedIndexHead.AsSpan(0, 32), decryptedOut, PaddingMode.None);
            if (written < 16) return false;

            // Unreal Pak Index başı: int32 mountLen + ASCII/UTF-16 MountPoint (örn. "../../../")
            int mountLen = BinaryPrimitives.ReadInt32LittleEndian(decryptedOut);
            bool isUtf16 = mountLen < 0;
            int absLen = Math.Abs(mountLen);
            if (absLen < 2 || absLen > 1024) return false;

            // MountPoint genelde '.' veya '/' ile başlar
            byte firstChar = decryptedOut[4];
            if (firstChar != (byte)'.' && firstChar != (byte)'/' && (firstChar < 32 || firstChar > 126))
                return false;

            if (isUtf16)
            {
                int charsToCheck = Math.Min(absLen - 1, 6);
                for (int i = 0; i < charsToCheck; i++)
                {
                    byte lo = decryptedOut[4 + i * 2];
                    byte hi = decryptedOut[4 + i * 2 + 1];
                    if (hi != 0 || lo < 32 || lo > 126) return false;
                }
            }
            else
            {
                int charsToCheck = Math.Min(absLen - 1, 12);
                for (int i = 0; i < charsToCheck; i++)
                {
                    byte b = decryptedOut[4 + i];
                    if (b < 32 || b > 126) return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateAndTestKey(string hexKey, Aes aesEcb, byte[]? pakTestBuffer)
    {
        if (!AesDecryptionHelper.ValidateKey(hexKey, out var keyBytes) || keyBytes.Length != 32)
            return false;

        // Kriptografik Rastgelelik Kontrolü: Gerçek bir AES anahtarında en az 18 benzersiz bayt olmalı
        var uniqueBytes = new HashSet<byte>(keyBytes);
        if (uniqueBytes.Count < 18) return false;

        // Null bayt sayısı 3'ü geçemez
        int zeroCount = 0;
        foreach (var b in keyBytes) if (b == 0) zeroCount++;
        if (zeroCount > 3) return false;

        // Okunabilir ASCII Metin Filtresi (DOS stub "This program...", Windows header vb. elenir)
        int asciiPrintable = 0;
        foreach (var b in keyBytes)
        {
            if (b >= 32 && b <= 126) asciiPrintable++;
        }
        if (asciiPrintable > 18) return false; // 32 baytın yarısından fazlası ASCII metinse elenir

        if (pakTestBuffer == null)
        {
            // Pak yoksa sadece bu yüksek entropili aday dönebilir
            return true;
        }

        Span<byte> decrypted = stackalloc byte[32];
        return TestCandidateKey(aesEcb, pakTestBuffer, keyBytes, decrypted);
    }

    private static bool IsHexChars(ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            bool isHex = (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    private static bool IsHexCharsUtf16(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i += 2)
        {
            byte b = span[i];
            byte high = span[i + 1];
            if (high != 0) return false;
            bool isHex = (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    /// <summary>
    /// Unreal .pak dosyasının footer'ını inceleyerek arşiv indeksinin şifreli olup olmadığını tespit eder.
    /// </summary>
    public static bool IsPakFileEncrypted(string pakFilePath)
    {
        try
        {
            using var fs = new FileStream(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 64) return false;

            int scanSize = (int)Math.Min(fs.Length, 8192);
            var tail = new byte[scanSize];
            fs.Seek(-scanSize, SeekOrigin.End);
            fs.ReadExactly(tail, 0, scanSize);

            for (int i = scanSize - 4; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) == 0x5A6F12E1)
                {
                    // V8+ (UE4.25 - UE5.4): bEncryptedIndex baytı Magic'ten hemen önceki bayttır
                    if (i > 0 && tail[i - 1] == 1) return true;
                    // V4-V7: bEncryptedIndex baytı SHA1(20) sonrası bayttır (+40)
                    if (i + 40 < scanSize && tail[i + 40] == 1) return true;
                    break;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Unreal .pak dosyasının sonundan footer'ı (0x5A6F12E1) arar, IndexOffset'i okur
    /// ve şifreli index bloğunun ilk 32 baytını döndürür.
    /// </summary>
    private static byte[]? ReadEncryptedIndexHead(string pakFilePath)
    {
        using var fs = new FileStream(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < 64) return null;

        int scanSize = (int)Math.Min(fs.Length, 8192);
        var tail = new byte[scanSize];
        fs.Seek(-scanSize, SeekOrigin.End);
        fs.ReadExactly(tail, 0, scanSize);

        for (int i = scanSize - 4; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) != 0x5A6F12E1) continue;
            if (i + 4 + 4 + 8 > scanSize) continue;

            long indexOffset = BitConverter.ToInt64(tail, i + 8); // Magic(4) + Version(4)
            if (indexOffset <= 0 || indexOffset + 32 > fs.Length) continue;

            var head = new byte[32];
            fs.Seek(indexOffset, SeekOrigin.Begin);
            fs.ReadExactly(head, 0, head.Length);
            return head;
        }

        return null;
    }

    /// <summary>
    /// Verilen sürecin (oyunun) klasör hiyerarşisinden otomatik olarak şifreli .pak dosyasını bulur.
    /// Şifresiz paketler yerine doğrudan şifreli olanları önceliklendirir.
    /// </summary>
    public static string? TryFindGamePakFile(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            var exePath = proc.MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;

            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir)) return null;

            var candidates = new List<string>();

            // 1. Exe klasörü ve 4 üst kademede Content/Paks ara (Klasik Unreal Engine dizini)
            var currentDir = new DirectoryInfo(dir);
            for (int i = 0; i < 4 && currentDir != null; i++)
            {
                var contentDir = Path.Combine(currentDir.FullName, "Content", "Paks");
                if (Directory.Exists(contentDir))
                {
                    candidates.AddRange(Directory.GetFiles(contentDir, "*.pak"));
                }

                // Alt klasörlerde Paks ara
                var paksDirs = currentDir.GetDirectories("Paks", SearchOption.AllDirectories);
                foreach (var pd in paksDirs)
                {
                    foreach (var f in pd.GetFiles("*.pak"))
                    {
                        candidates.Add(f.FullName);
                    }
                }

                currentDir = currentDir.Parent;
            }

            // 2. Exe'nin kök klasörlerinde *.pak ara
            currentDir = new DirectoryInfo(dir);
            for (int i = 0; i < 3 && currentDir != null; i++)
            {
                foreach (var f in currentDir.GetFiles("*.pak", SearchOption.TopDirectoryOnly))
                {
                    candidates.Add(f.FullName);
                }
                currentDir = currentDir.Parent;
            }

            if (candidates.Count == 0) return null;

            // ÖNCELİK 1: Şifreli olan (.pak) dosyasını bul
            var encryptedPak = candidates.Distinct().FirstOrDefault(IsPakFileEncrypted);
            if (!string.IsNullOrEmpty(encryptedPak))
                return encryptedPak;

            // ÖNCELİK 2: "pakchunk" içeren ana oyun paketini bul
            var pakchunk = candidates.Distinct().FirstOrDefault(p => Path.GetFileName(p).Contains("pakchunk", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(pakchunk))
                return pakchunk;

            return candidates.Distinct().FirstOrDefault();
        }
        catch { }

        return null;
    }
}
