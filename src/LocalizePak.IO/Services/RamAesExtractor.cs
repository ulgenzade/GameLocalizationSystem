using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
/// Oyunun çalışan RAM belleğini (ReadProcessMemory) güvenli bir şekilde tarayarak
/// 256-bit (32 bayt) AES anahtarını yakalayan ve .pak ile doğrulayan servis.
/// </summary>
public static class RamAesExtractor
{
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_READONLY = 0x02;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_EXECUTE_READ = 0x20;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

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
    /// Seçili sürecin belleğini tarayarak 256-bit AES anahtar adaylarını tespit eder.
    /// </summary>
    public static async Task<string?> ScanProcessForAesKeyAsync(
        int processId,
        string? pakFilePath = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
            if (hProcess == IntPtr.Zero)
                throw new UnauthorizedAccessException("Oyun sürecine erişilemedi. Lütfen GLS'yi Yönetici Olarak (Run as Administrator) çalıştırmayı deneyin.");

            // Unreal .pak'ta footer düz metindir; şifreli olan index bloğudur.
            // Footer'dan IndexOffset'i bulup index'in ilk 32 baytını bir kez belleğe alıyoruz.
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
                IntPtr address = IntPtr.Zero;
                long maxAddress = 0x7FFFFFFF0000; // 64-bit kullanıcı alanı sınırı
                int memInfoSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

                var candidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byte[] chunkBuffer = new byte[65536];

                int scannedPages = 0;

                while (address.ToInt64() < maxAddress)
                {
                    ct.ThrowIfCancellationRequested();

                    if (VirtualQueryEx(hProcess, address, out var memInfo, (uint)memInfoSize) == 0)
                        break;

                    bool isValidState = memInfo.State == MEM_COMMIT;
                    bool isValidProtect = (memInfo.Protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE)) != 0;

                    if (isValidState && isValidProtect && memInfo.RegionSize.ToInt64() > 0 && memInfo.RegionSize.ToInt64() < 100 * 1024 * 1024)
                    {
                        long regionSize = memInfo.RegionSize.ToInt64();
                        long readOffset = 0;

                        while (readOffset < regionSize)
                        {
                            int bytesToRead = (int)Math.Min(chunkBuffer.Length, regionSize - readOffset);
                            IntPtr readAddr = new IntPtr(memInfo.BaseAddress.ToInt64() + readOffset);

                            if (ReadProcessMemory(hProcess, readAddr, chunkBuffer, bytesToRead, out var bytesRead) && bytesRead.ToInt64() >= 32)
                            {
                                int readCount = (int)bytesRead.ToInt64();
                                var foundKey = ScanBufferForKeys(chunkBuffer, readCount, candidateKeys, pakTestBuffer);

                                if (!string.IsNullOrEmpty(foundKey))
                                {
                                    return foundKey;
                                }
                            }

                            readOffset += bytesToRead;
                        }
                    }

                    scannedPages++;
                    if (scannedPages % 50 == 0)
                    {
                        int percent = (int)Math.Min(99, (address.ToInt64() / (double)maxAddress) * 100);
                        progress?.Report(percent);
                    }

                    address = new IntPtr(memInfo.BaseAddress.ToInt64() + memInfo.RegionSize.ToInt64());
                }

                // Kesin doğrulama yapılamadıysa veya pak yoksa ilk adayı döndür
                return candidateKeys.FirstOrDefault();
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }, ct);
    }

    private static string? ScanBufferForKeys(byte[] buffer, int length, HashSet<string> candidateKeys, byte[]? pakTestBuffer)
    {
        // 32 baytlık yüksek entropili blokları tara
        for (int i = 0; i <= length - 32; i += 4)
        {
            // Null bayt yoğunluğu kontrolü (şifreler rastgele 32 bayttır)
            int zeroCount = 0;
            for (int j = 0; j < 32; j++)
            {
                if (buffer[i + j] == 0) zeroCount++;
            }

            if (zeroCount > 6) continue; // Fazla null içeren sıradan bellek alanlarını atla

            var candidate = "0x" + Convert.ToHexString(buffer, i, 32);

            if (pakTestBuffer != null)
            {
                if (TestKeyAgainstPakBuffer(candidate, pakTestBuffer))
                {
                    candidateKeys.Add(candidate);
                    return candidate;
                }
            }
            else
            {
                if (candidateKeys.Count < 50)
                    candidateKeys.Add(candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Unreal footer'ını (düz metin) dosya sonunda arar, IndexOffset'i okur ve
    /// şifreli index bloğunun ilk 32 baytını döndürür. Bulunamazsa null.
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

    private static bool TestKeyAgainstPakBuffer(string hexKey, byte[] encryptedIndexHead)
    {
        try
        {
            if (!AesDecryptionHelper.ValidateKey(hexKey, out var keyBytes) || keyBytes == null)
                return false;

            if (encryptedIndexHead.Length < 32) return false;

            var decrypted = AesDecryptionHelper.Decrypt(encryptedIndexHead, keyBytes);
            if (decrypted == null || decrypted.Length < 16) return false;

            // Index, MountPoint FString'i ile başlar: int32 uzunluk (null dahil) + ASCII karakterler (genelde "../../../")
            int mountLen = BitConverter.ToInt32(decrypted, 0);
            if (mountLen < 2 || mountLen > 1024) return false;

            int charsToCheck = Math.Min(mountLen - 1, decrypted.Length - 4);

            for (int i = 0; i < charsToCheck; i++)
            {
                byte b = decrypted[4 + i];
                if (b < 32 || b > 126) return false;
            }

            return charsToCheck >= 2;
        }
        catch { }

        return false;
    }
}
