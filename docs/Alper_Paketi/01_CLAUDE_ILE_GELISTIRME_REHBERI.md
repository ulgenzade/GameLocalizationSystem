# 🤖 Alper & Claude Geliştirme ve İşbirliği Rehberi

> **Amaç:** Alper'in projeyi Git'ten klonladıktan sonra Claude (Claude 3.5 Sonnet / Claude 3.7 Sonnet) ile en verimli, hatasız ve hızlı şekilde çalışmasını sağlamak.

---

## 🚀 1. Hızlı Başlangıç (Git ve Ortam Kurulumu)

### Adım 1: Projeyi Klonla
```bash
git clone https://github.com/ulgenzade/GameLocalizationSystem.git
cd GameLocalizationSystem
```

### Adım 2: Gerekli Ortam
- **.NET 8.0 SDK** (veya üzeri)
- **Visual Studio 2022 / VS Code / JetBrains Rider**
- **Avalonia for Visual Studio / VS Code Extension** (XAML önizleme ve derleme desteği)

### Adım 3: Testleri Çalıştır ve Doğrula
Projeyi klonlar klonlamaz testlerin %100 yeşil olduğunu doğrula:
```bash
dotnet test src/tests/LocalizePak.Tests/LocalizePak.Tests.csproj
```
*(Şu an 116/116 test başarıyla geçmektedir).*

### Adım 4: Uygulamayı Çalıştır
```bash
dotnet run --project src/LocalizePak.UI/LocalizePak.UI.csproj
```

---

## 🧠 2. Claude'a Verilecek Sistem İstemi (System Prompt / Başlangıç Talimatı)

Alper, projede yeni bir özelliğe veya göreve başlamadan önce Claude'a aşağıdaki bloğu doğrudan yapıştırabilir:

```text
Merhaba Claude, ben GameLocalizationSystem (GLS) projesi üzerinde çalışıyorum.
Bu proje; .NET 8, Avalonia UI (v11+), CommunityToolkit.Mvvm ve CUE4Parse kullanan, 
video oyunu yerelleştirme ve tersine mühendislik (Reverse Engineering) platformudur.

LÜTFEN ŞU MİMARİ KURALLARA KESİNLİKLE UY:
1. Avalonia UI v11 ve Windows 11 Fluent 2 standartlarını koru. XAML derleyici denetimlerini (x:DataType="vm:MainViewModel") bozma.
2. MainViewModel monolitik değildir; partial class yapısındadır (MainViewModel.Archive.cs, MainViewModel.Translation.cs, MainViewModel.Filter.cs, MainViewModel.Export.cs).
3. Unreal Engine tarafında asla hardcoded motor sürümü (EGame) kullanma; UnrealCuePakHandler içindeki DetectPakVersion ve DetectEngineVersion dinamik algılayıcılarını kullan.
4. Dosya formatlarında (LocresCodec, PoCodec, UAssetStringPatcher) Little Endian uzunluk baytlarına ve null sonlandırıcılara (%100 UTF-8 / UTF-16LE) dikkat et.
5. Her yeni eklediğin fonksiyona LocalizePak.Tests altında xUnit testi yaz ve 'dotnet test src/tests/LocalizePak.Tests/LocalizePak.Tests.csproj' komutunun %100 yeşil kaldığını doğrula.
6. Yanıtlarında Türkçe konuş ve Türkçe yaz.
```

---

## 🛠️ 3. Sık Kullanılan CLI Komutları

| İşlem | Komut |
| :--- | :--- |
| **Tüm Çözümü Derleme** | `dotnet build src/LocalizePak.slnx` |
| **Birim Testlerini Koşma** | `dotnet test src/tests/LocalizePak.Tests/LocalizePak.Tests.csproj` |
| **Masaüstü Arayüzünü Başlatma** | `dotnet run --project src/LocalizePak.UI/LocalizePak.UI.csproj` |
| **Konsol CLI Aracını Başlatma** | `dotnet run --project src/LocalizePak.Cli/LocalizePak.Cli.csproj -- --help` |
| **Tek Parça Portable .EXE Paketleme** | `dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/GLS_SingleFile` |

---

## 🎯 4. Alper'in Üzerinde Çalışabileceği Açık Görevler (Roadmap Fırsatları)

1. **UE5 IoStore Konteynerleri (`.utoc` / `.ucas`):**
   - CUE4Parse okuyucusunu IoStore sanal blok konteynerleri için tam genişletmek ve `_P.utoc` yama üretimi prototipi yazmak.
2. **Unity Deep Asset Extractor (`UnityAssetExtractor`):**
   - Unity'nin `TextAsset`, `MonoBehaviour` ve `StringTable` koleksiyonlarını doğrudan açan C# ayrıştırıcısı yazmak.
3. **DataGrid Live LQA Görsel Rozetleri:**
   - DataGrid satırlarında MQM/DQF hata durumu olan satırlara canlı uyarı ikonu ve tooltip açıklaması eklemek.
4. **Context Caching (Gemini & Anthropic):**
   - Oyun sözlüğü (Glossary) ve evren bilgisini (Lore) model önbelleğine alarak token maliyetini %80 düşüren kancayı bağlamak.
