# 🛠️ TECH_STACK.md — Teknoloji Yığını Analizi

---

## Seçilen Teknoloji: C# (.NET 8/9) + Avalonia UI

### Neden C# + Avalonia?

| Kriter | C# + Avalonia | Rust + Tauri | Python + PySide6 | C++ + Qt6 |
|---|---|---|---|---|
| **Çalışma Zamanı Performansı** | Çok Yüksek (JIT/AOT) | En Yüksek (Yerel) | Düşük/Orta | En Yüksek (Yerel) |
| **Bellek Tüketimi** | Orta (~80–150 MB) | Düşük (~30–60 MB) | Yüksek (~200–400 MB) | Düşük (~40–80 MB) |
| **Binary Dosya İşleme** | ✅ Span<T>, CUE4Parse | ✅ repak C-ABI | ⚠️ struct, pyuepak | ✅ Manuel pointer |
| **Çapraz Platform** | ✅ Win/Mac/Linux | ✅ Win/Mac/Linux | ✅ Win/Mac/Linux | ✅ Win/Mac/Linux |
| **Hazır .pak Kütüphanesi** | ✅ Unpaker, CUE4Parse | ⚠️ FFI köprüsü | ⚠️ pyuepak yavaş | ❌ Manuel |
| **Hazır .locres Kütüphanesi** | ✅ LocresLib | ❌ Yok | ⚠️ pylocres sınırlı | ❌ Yok |
| **async/await HTTP** | ✅ HttpClientFactory | ✅ reqwest | ✅ aiohttp | ⚠️ QNetworkAccessManager |
| **DataGrid Sanallaştırma** | ✅ Avalonia DataGrid | ⚠️ Webview2 sınırlı | ✅ QTableView | ✅ QTableView |
| **Geliştirme Verimliliği** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ |

---

## NuGet Bağımlılıkları

### LocalizePak.Core

```xml
<!-- Temel domain modelleri — ek bağımlılık yok -->
```

### LocalizePak.IO

```xml
<PackageReference Include="CUE4Parse" Version="*" />
<PackageReference Include="K4os.Compression.LZ4" Version="*" />
<PackageReference Include="ZstdSharp.Port" Version="*" />
<PackageReference Include="System.IO.Hashing" Version="*" />
```

### LocalizePak.Translation

```xml
<PackageReference Include="Microsoft.Extensions.Http" Version="*" />
<PackageReference Include="System.Text.Json" Version="*" />
<PackageReference Include="Polly" Version="*" />
```

### LocalizePak.UI

```xml
<PackageReference Include="Avalonia" Version="11.*" />
<PackageReference Include="Avalonia.Desktop" Version="11.*" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="*" />
```

---

## Mimari Kararlar ve Gerekçeler

### 1. Zero-Allocation Parsing
`Span<T>` ve `Memory<T>` kullanımı ile GC baskısı minimize edilir:
```csharp
// Kötü — kopyalama yapar
byte[] data = File.ReadAllBytes(path);

// İyi — sıfır kopyalamalı
using var mmf = MemoryMappedFile.CreateFromFile(path);
using var accessor = mmf.CreateViewAccessor();
// Span<byte> üzerinden okuma
```

### 2. Async/Await Her Yerde
UI donmaması için tüm I/O operasyonları async:
```csharp
// UI thread'i bloklamaz
var entries = await _pakService.OpenAsync(path, ct);
```

### 3. Polly ile Retry Politikası
Rate-limit (429) için Exponential Backoff:
```csharp
var policy = Policy
    .Handle<HttpRequestException>(e => e.StatusCode == HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(5, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
```

### 4. Dependency Injection
`Microsoft.Extensions.DependencyInjection` ile gevşek bağlama:
```csharp
services.AddSingleton<IPakReader, PakService>();
services.AddSingleton<ILocresSerializer, LocresService>();
services.AddTransient<ITranslationProvider, GeminiTranslationProvider>();
```

---

## .NET 8 vs .NET 9

| Özellik | .NET 8 (LTS) | .NET 9 |
|---|---|---|
| Destek süresi | 2026 sonuna kadar | 2025 Mayıs |
| AOT desteği | ✅ | ✅ Geliştirilmiş |
| GC iyileştirmeleri | Temel | Gelişmiş |
| **Öneri** | ✅ Üretim için seç | Deneysel |

> Proje **hedef çerçeve** olarak `net10.0` kullanır (LTS — Uzun Vadeli Destek). `net8.0`'dan geçişin nedeni:
> UE 5.4+ paketlerini (pak v12 `Utf8PakDirectory`, yeni IoStore sürümleri) okuyan güncel CUE4Parse sürümleri yalnızca .NET 10 hedefler.
