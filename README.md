# 🎮 GLS — Game Localization System

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia%20UI-11.2%2B-purple.svg)](https://avaloniaui.net/)
[![WinUI 3 Fluent 2](https://img.shields.io/badge/UI-Fluent%202%20Tabs-0078D4.svg)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/ulgenzade/GameLocalizationSystem?color=orange&label=Release)](https://github.com/ulgenzade/GameLocalizationSystem/releases/latest)

> 🇹🇷 **TR:** **GLS (Game Localization System)**, video oyunlarının arşiv dosyalarını (`.pak`, `.locres`, `.json`, `.csv`, `.xml`) doğrudan ayrıştıran, kod değişkenlerini ve format etiketlerini koruyarak gelişmiş yapay zeka modelleri (OpenRouter, Gemini, Together AI, Mistral, Claude, OpenAI, DeepSeek, Ollama) ile edebi seviyede çeviren ve tek tıkla yamalanabilir arşivler üreten profesyonel bir masaüstü oyun yerelleştirme yazılımıdır.
>
> 🇬🇧 **EN:** **GLS (Game Localization System)** is a professional desktop game localization suite that parses game archives (`.pak`, `.locres`, `.json`, `.csv`, `.xml`) directly, protects syntax tags & variables, translates game dialogue into literary-grade prose using cutting-edge AI models (OpenRouter, Gemini, Together AI, Mistral, Claude, OpenAI, DeepSeek, Ollama), and generates one-click patchable mod archives.

---

### 🌐 Dil Seçimi / Language Selection

Lütfen ayrıntılı dokümantasyonu görüntülemek için tercih ettiğiniz dili açın:  
*Please expand your preferred language below to view the full documentation:*

---

<details open>
<summary><b>🇹🇷 Türkçe Dokümantasyon (Görüntülemek için tıklayın)</b></summary>
<br>

## 🌟 Temel Özellikler

### 1. 🤖 Kapsamlı AI & LLM Motor Desteği
- **Hepsi Bir Arada Gateway'ler:** 
  - **OpenRouter (`openrouter.ai`)**: Tek API anahtarıyla yüzlerce modele erişim.
  - **Together AI (`together.ai`)**: Ultra hızlı açık kaynak havuzu (Llama 3.3 Turbo, DeepSeek V3/R1, Qwen 2.5 72B).
  - **DeepInfra (`deepinfra.com`)**: Yüksek kotalı ve ekonomik açık kaynak sunucular.
  - **Perplexity AI (`perplexity.ai`)**: Sonar Pro ve Sonar modelleriyle gerçek zamanlı web doğrulama.
- **Resmi AI Sağlayıcıları:**
  - **Google Gemini** (Gemini 2.5 Pro, 2.0 Flash)
  - **Groq Cloud** (GPT-OSS, Qwen, Llama ultra-düşük gecikmeli çıkarım)
  - **Anthropic Claude** (Claude 3.7 Sonnet, Claude 3.5 Sonnet & Haiku)
  - **OpenAI** (GPT-4o, GPT-4o Mini, o1/o3 serisi)
  - **DeepSeek** (DeepSeek Chat V3, DeepSeek Reasoner R1)
  - **Moonshot Kimi & Alibaba Qwen**
- **📥 Yerel AI Model Hub (Ollama Entegrasyonu):**
  - İnternetsiz ve API kotası harcamadan yerel modelleri (Qwen 2.5, DeepSeek R1, Llama 3.2, Mistral) tek tıkla indirip çalıştırma merkezi.
- **🧠 Düşünme & Akıl Yürütme Modu (Reasoning Effort):**
  - Edebi diyaloglar ve deyim analizleri için Low / Medium / High düşünme derinliği ayarı.

### 2. 🛡️ Akıllı Kod & Değişken Maskeleme (Masking Engine)
- Oyun diyaloglarındaki format kodlarını (`{0}`, `%s`, `{PlayerName}`, `\n`, `\t`, `<color>`, `</color>`, `[b]`, `[/b]`) regex tabanlı geçici belirteçlerle (`[VAR_0]`, `[TAG_0]`) korur; çeviri sonrasında bayt hassasiyetiyle eski yerine yerleştirir.
- Oyunların çökmesini (crash) ve değişken bozulmalarını %100 engeller.

### 3. 🌐 Çok Dilli Master Sistem Promptu & Dil Ayarları
- **Desteklenen Diller:** **Türkçe**, **English (İngilizce)**, **Русский (Rusça)** ve **简体中文 (Çince)**.
- Her hedef dil için özel karakter analizi, argo/sokak dili, soylu/saray hitapları ve eşya/yetenek tutarlılığı kurallarını içeren hazır şablonlar.
- Kullanıcılar prompt şablonunu dilediği dilde yükleyebilir ve arayüz üzerinden özgürce düzenleyebilir.

### 4. 🗂️ Oyun Motoru & Arşiv Desteği
- **Unreal Engine:** `.pak` (V1 - V11+) ve `.locres` (V1, V2, V3) ikili lokalizasyon tabloları.
- **Larian Studios (Divinity / Baldur's Gate):** `.pak` (LSPak V7 - V18) ve XML diyalog ağaçları.
- **Evrensel Formatlar:** `.json`, `.csv`, `.tsv`, `.po` ve düz metin dosyaları.
- **Orijinal Dosya Koruması:** Arşiv içerisindeki fontlar, sesler ve dokunulmayan ikili varlıklar bayt bayt korunarak yama paketi üretilir.

### 5. 🔑 Canlı Oyundan AES-256 Çıkarıcı (RAM Dumper)
- Çalışan oyun sürecinin RAM belleğini (`ReadProcessMemory`) tarayarak şifreli `.pak` arşivlerinin AES-256 anahtarını otomatik tespit eder.
- Yapay zeka destekli Oyun Motoru ve bilinen AES Dedektifi.

### 6. 📊 Kalite Kontrol (QA) & İstatistik Paneli
- **Eksik Değişken & Etiket Taraması (F7):** Unutulan değişkenleri tespit eder ve tek tıkla otomatik onarır.
- **UI Taşma Riski Analizi:** Orijinalinden %40 daha uzun metinleri işaretleyerek arayüz taşmalarını önler.
- **KPI Metrikleri:** Toplam satır, kelime ve karakter hacmi, tamamlanma yüzdesi, canlı çeviri hızı (satır/sn) ve tahmini kalan süre.

---

## 🏗️ Kullanılan Ajan Becerileri & Mimarileri (Agent Skills)

Projenin geliştirilmesinde aşağıdaki uzmanlık becerileri (`.agents/skills`) kullanılmıştır:

| Skill | Açıklama |
| :--- | :--- |
| **`avalonia-fluent-architect`** | Avalonia UI 11.2+, CommunityToolkit.Mvvm, Windows 11 Fluent 2 / WinUI 3 sekmeli (`TabControl`) ve akordeon kart (`Expander`) tasarımı, Mica/Acrylic teması ve bellek sızıntısız asenkron UI yönetimi. |
| **`dotnet-desktop-singlefile-packaging`** | .NET 8 SDK kullanılarak harici bağımlılık gerektirmeyen, sıkıştırılmış, yerel kitaplıkları gömülü tek parça Windows masaüstü yürütülebilir dosyası (`dist/GLS/GLS.exe`) derleme mimarisi. |
| **`game-ai-localization-orchestrator`** | Çoklu LLM sağlayıcılarında eşzamanlı toplu iş (batch) çevirisi, exponential backoff, rate-limit yönetimi, Translation Memory (TM) önbelleği ve edebi oyun diyalog adaptasyon kuralları. |
| **`game-pak-reverse-engineering`** | Unreal Engine ve Larian arşiv ikili yapısı analizi, AES-256 şifre çözümü, Oodle/LZ4/Zstd sıkıştırma algoritmaları ve Windows API bellek tarama mekanizması. |
| **`locres-localization-parser`** | Unreal Engine ikili `.locres` formatı (V1-V3), namespace/hash üretimi ve CSV/JSON evrensel lokalizasyon ayrıştırma motoru. |

---

## 🔒 Güvenlik & Gizlilik (Security & Secrets)

- **Sıfır Hardcoded Secret:** Proje kod tabanında, git geçmişinde veya kaynak dosyalarında hiçbir API anahtarı veya şifre yer almaz.
- **İzole Ayar Depolama:** Kullanıcının girdiği tüm API anahtarları yalnızca kullanıcının yerel Windows oturumunda (`%APPDATA%\LocalizePak\settings.json`) saklanır. Repo paylaşıldığında kimsenin anahtarı dışarı sızmaz.
- `.gitignore` dosyası yerel ayarları, gizli dosyaları ve derleme kalıntılarını tam kapsamlı şekilde izole eder.

---

## 🚀 Kurulum ve Derleme

### Gereksinimler
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (1809+) veya Windows 11 (Tavsiye edilen)

### 1. Hazır Sürümü İndirme (Tavsiye Edilen)
Herhangi bir kurulum veya .NET SDK yüklemeden doğrudan kullanmak için [Releases](https://github.com/ulgenzade/GameLocalizationSystem/releases/latest) sayfasından `GLS.exe` dosyasını indirin.

### 2. Kaynak Koddan Derleme
```bash
git clone https://github.com/ulgenzade/GameLocalizationSystem.git
cd GameLocalizationSystem
dotnet build
dotnet test
```

### 3. Tek Dosya (.exe) Paketleme (Publish)
```powershell
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  --self-contained true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist/GLS
```

---

## ⌨️ Klavye Kısayolları (Hotkeys)

| Kısayol | İşlev |
| :--- | :--- |
| **F5** | Çevirileri baştan başlat |
| **F6** | Kaldığı yerden devam et |
| **F7** | Kalite Kontrolü (QA) çalıştır |
| **Esc** | Aktif çeviriyi durdur |
| **Ctrl + F / Ctrl + H** | Toplu Bul ve Değiştir panelini aç/kapat |
| **Ctrl + O** | `.pak` Arşivi aç |
| **Ctrl + Shift + O** | Tekil Lokalizasyon Dosyası (`.locres`, `.json`, `.csv`) aç |
| **Ctrl + E** | Orijinal isimle tarihli izole klasöre dışa aktar |
| **Ctrl + S** | Doğrudan farklı kaydet |
| **Ctrl + Shift + T** | Yalnızca seçili satırları çevir |
| **Ctrl + R** | Yalnızca hatalı satırları yeniden dene |

</details>

---

<details>
<summary><b>🇬🇧 English Documentation (Click to expand)</b></summary>
<br>

## 🌟 Key Features

### 1. 🤖 Comprehensive AI & LLM Engine Support
- **All-in-One Gateways:**
  - **OpenRouter (`openrouter.ai`)**: Access to hundreds of frontier models with a single API key.
  - **Together AI (`together.ai`)**: Ultra-fast open-source cluster (Llama 3.3 Turbo, DeepSeek V3/R1, Qwen 2.5 72B).
  - **DeepInfra (`deepinfra.com`)**: Cost-effective, high-quota open-source model hosting.
  - **Perplexity AI (`perplexity.ai`)**: Real-time web-grounded validation using Sonar and Sonar Pro.
- **First-Party AI Providers:**
  - **Google Gemini** (Gemini 2.5 Pro, 2.0 Flash)
  - **Groq Cloud** (Ultra-low latency inference for Llama, Qwen, and custom models)
  - **Anthropic Claude** (Claude 3.7 Sonnet, Claude 3.5 Sonnet & Haiku)
  - **OpenAI** (GPT-4o, GPT-4o Mini, o1/o3 series)
  - **DeepSeek** (DeepSeek Chat V3, DeepSeek Reasoner R1)
  - **Moonshot Kimi & Alibaba Qwen**
- **📥 Local AI Model Hub (Ollama Integration):**
  - Download and run models locally (Qwen 2.5, DeepSeek R1, Llama 3.2, Mistral) without internet or API fees.
- **🧠 Reasoning Effort Configuration:**
  - Fine-tune reasoning depth (Low / Medium / High) for complex game lore, archaic dialects, and intricate riddles.

### 2. 🛡️ Intelligent Tag & Variable Masking Engine
- Automatically masks in-game code variables (`{0}`, `%s`, `{PlayerName}`, `\n`, `\t`, `<color>`, `</color>`, `[b]`, `[/b]`) with temporary tokens (`[VAR_0]`, `[TAG_0]`) before passing strings to LLMs.
- Restores them post-translation with byte-exact precision, preventing game engine crashes and syntax corruption.

### 3. 🌐 Multilingual Master System Prompts & App Localization
- **Supported Languages:** **Turkish**, **English**, **Russian**, and **Simplified Chinese**.
- Curated prompt templates tailored for dialect nuances, street slang, royal decorum, item consistency, and gender markers.
- Prompts can be customized, edited, and saved per project in the settings.

### 4. 🗂️ Game Engine & Archive Support
- **Unreal Engine:** `.pak` (Versions 1 through 11+) and binary `.locres` (V1, V2, V3) string tables.
- **Larian Studios (Divinity / Baldur's Gate):** `.pak` (LSPak V7 through V18) and XML dialogue trees.
- **Universal Formats:** `.json`, `.csv`, `.tsv`, `.po`, and raw plain-text.
- **Asset Integrity:** Preserves fonts, textures, audio, and untouched binary entries byte-for-byte in repacked mod archives.

### 5. 🔑 Live Process AES-256 Key Extractor (RAM Dumper)
- Scans live game process memory (`ReadProcessMemory`) to extract AES-256 encryption keys required to unpack encrypted `.pak` files.

### 6. 📊 Quality Assurance (QA) & Telemetry
- **Missing Variable & Tag Audit (F7):** Flags missed or altered variable tags and provides 1-click auto-repair.
- **UI Overflow Warning:** Warns if translations expand >40% compared to source strings, preventing UI clipping.
- **Live KPI Metrics:** Real-time speed (lines/sec), total character/word counts, completion percentage, and estimated time to completion (ETC).

---

## 🏗️ Architecture & Agent Skills

This repository was architected and built utilizing specialized developer agent skills (`.agents/skills`):

| Skill | Description |
| :--- | :--- |
| **`avalonia-fluent-architect`** | Avalonia UI 11.2+, CommunityToolkit.Mvvm, Windows 11 Fluent 2 / WinUI 3 tabbed navigation (`TabControl`), collapsible card sections (`Expander`), Mica/Acrylic theming, and leak-free async dispatching. |
| **`dotnet-desktop-singlefile-packaging`** | .NET 8 SDK single-file self-contained desktop packaging (`dist/GLS/GLS.exe`) with compressed native assemblies and zero runtime dependencies. |
| **`game-ai-localization-orchestrator`** | Multi-provider LLM concurrency orchestrator, adaptive rate-limiting, exponential backoff, Translation Memory (TM) cache, and literary adaptation rules. |
| **`game-pak-reverse-engineering`** | Unreal Engine & Larian archive binary structure parsers, AES-256 decryption, Oodle/LZ4/Zstd decompression algorithms, and Windows API memory scanning. |
| **`locres-localization-parser`** | Unreal Engine binary `.locres` serializer/parser (V1-V3), CRC/namespace hash computation, and delimited CSV/JSON string table drivers. |

---

## 🔒 Security & Privacy

- **Zero Hardcoded Secrets:** No API keys, credentials, or private paths exist in the codebase or git history.
- **Isolated User Storage:** All user-entered API credentials are stored securely on the local machine under `%APPDATA%\LocalizePak\settings.json`.
- `.gitignore` strictly excludes local configuration files, build binaries, and temporary artifacts.

---

## 🚀 Installation & Build

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (1809+) or Windows 11 (Recommended)

### 1. Download Pre-built Binary (Recommended)
Download the standalone `GLS.exe` directly from the [GitHub Releases](https://github.com/ulgenzade/GameLocalizationSystem/releases/latest) page. No .NET runtime installation required.

### 2. Building from Source
```bash
git clone https://github.com/ulgenzade/GameLocalizationSystem.git
cd GameLocalizationSystem
dotnet build
dotnet test
```

### 3. Single-File Packaging
```powershell
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  --self-contained true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist/GLS
```

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **F5** | Start translation from the beginning |
| **F6** | Resume translation from where it stopped |
| **F7** | Run Quality Assurance (QA) inspection |
| **Esc** | Abort active translation task |
| **Ctrl + F / Ctrl + H** | Toggle Find and Replace drawer |
| **Ctrl + O** | Open `.pak` archive |
| **Ctrl + Shift + O** | Open single localization file (`.locres`, `.json`, `.csv`) |
| **Ctrl + E** | Export translation into isolated timestamped folder |
| **Ctrl + S** | Save As directly |
| **Ctrl + Shift + T** | Translate selected rows only |
| **Ctrl + R** | Retry failed rows only |

</details>

---

## 📄 Lisans / License
Bu proje [MIT Lisansı](LICENSE) kapsamında açık kaynak olarak lisanslanmıştır.  
*This project is open-source and licensed under the [MIT License](LICENSE).*
