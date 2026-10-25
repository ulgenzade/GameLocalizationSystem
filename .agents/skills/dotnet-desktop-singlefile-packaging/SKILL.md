---
name: dotnet-desktop-singlefile-packaging
description: >-
  Builds, packages, and optimizes self-contained single-file Windows desktop applications
  using .NET 10 SDK, Avalonia UI, and dotnet publish CLI commands.
  Use when compiling distribution binaries, creating single .exe executables, or optimizing binary startup time.
---

# .NET 10 Self-Contained Desktop Single-File Packaging Guide

Bu rehber, **GameLocalizationSystem (GLS)** uygulamasının kullanıcı bilgisayarında hiçbir .NET SDK veya çalışma ortamı (runtime) kurulu olmasa dahi tek tıkla çalışabilen bağımsız (Self-Contained) tek dosya (`GLS.exe`) olarak paketlenmesi için kullanılır.

## 🚀 Dağıtım (Publish) Komutları

### 1. Standart Bağımsız Tek Dosya (Self-Contained Win-x64)
Kullanıcıların en sorunsuz çalıştıracağı varsayılan üretim modu:
```powershell
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist/GLS
```

### 2. Hızlı Başlatma Optimizasyonu (ReadyToRun - R2R)
Uygulama açılış süresini (startup time) düşürmek için Ahead-of-Time (AOT) derleme:
```powershell
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/GLS
```

## 📦 Dağıtım Öncesi Doğrulama Kontrol Listesi

1. **Native DLL'ler:**
   - Proje `oo2core_9_win64.dll` gibi yerel kütüphaneler kullanıyorsa, bu dosyaların çıktı dizinine kopyalandığı (`CopyToOutputDirectory="PreserveNewest"`) doğrulanmalıdır.
2. **Avalonia XAML Varlıkları:**
   - XAML ve ikon varlıklarının `AvaloniaResource` olarak projeye gömülü olduğu kontrol edilmelidir.
3. **xUnit Birim Testleri:**
   - Paketleme öncesinde daima tüm testlerin yeşil olduğu teyit edilmelidir:
     ```powershell
     dotnet test src/LocalizePak.slnx -c Release
     ```
