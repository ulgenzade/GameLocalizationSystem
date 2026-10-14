# 🎮 GLS — Game Localization System

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia%20UI-11.2%2B-purple.svg)](https://avaloniaui.net/)
[![Platform: Windows & Linux](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux-brightgreen.svg)]()
[![WinUI 3 Fluent 2](https://img.shields.io/badge/UI-Fluent%202%20Tabs-0078D4.svg)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/ulgenzade/GameLocalizationSystem?color=orange&label=Release)](https://github.com/ulgenzade/GameLocalizationSystem/releases/latest)

> 🇹🇷 **TR:** **GLS (Game Localization System)**, video oyunlarının arşiv dosyalarını (`.pak`, `.locres`, `.json`, `.csv`, `.xml`) doğrudan ayrıştıran, kod değişkenlerini ve format etiketlerini koruyarak gelişmiş yapay zeka modelleri (OpenRouter, Gemini, Together AI, Mistral, Claude, OpenAI, DeepSeek, Ollama) ile edebi seviyede çeviren ve tek tıkla yamalanabilir arşivler üreten **Windows ve Linux** destekli profesyonel bir masaüstü oyun yerelleştirme yazılımıdır.
>
> 🇬🇧 **EN:** **GLS (Game Localization System)** is a cross-platform (**Windows & Linux**) desktop game localization suite that parses game archives (`.pak`, `.locres`, `.json`, `.csv`, `.xml`) directly, protects syntax tags & variables, translates game dialogue into literary-grade prose using cutting-edge AI models (OpenRouter, Gemini, Together AI, Mistral, Claude, OpenAI, DeepSeek, Ollama), and generates one-click patchable mod archives.

---

### 🌐 Dil Seçimi / Language Selection

Lütfen ayrıntılı dokümantasyonu görüntülemek için tercih ettiğiniz dili açın:  
*Please expand your preferred language below to view the full documentation:*

---

<details open>
<summary><b>🇹🇷 Türkçe Dokümantasyon (Görüntülemek için tıklayın)</b></summary>
<br>

## 🌟 Temel Özellikler

### 1. 🐧 Tam Çapraz Platform (Windows & Linux Desteği)
- **Windows (x64):** Windows 10 & 11 üzerinde yerel Mica/Acrylic efektli modern WinUI 3 Fluent arayüzü.
- **Linux (x64):** Ubuntu, Debian, Fedora, Arch Linux ve SteamOS (Steam Deck) üzerinde harici runtime veya .NET gerektirmeden çalışan yerel X11/Wayland desteği.
- **Masaüstü Entegrasyonu:** Linux için hazır `.desktop` başlatıcı ve simge desteği.

### 2. 🤖 Kapsamlı AI & LLM Motor Desteği
- **Hepsi Bir Arada Gateway'ler:** 
  - **OpenRouter (`openrouter.ai`)**: Tek API anahtarıyla yüzlerce modele erişim.
  - **Together AI (`together.ai`)**: Ultra hızlı açık kaynak havuzu (Llama 3.3 Turbo, DeepSeek V3/R1, Qwen 2.5 72B).
  - **DeepInfra (`deepinfra.com`)**: Yüksek kotalı ve ekonomik açık kaynak sunucular.
  - **Perplexity AI (`perplexity.ai`)**: Sonar Pro ve Sonar modelleriyle gerçek zamanlı web doğrulama.
- **Resmi AI Sağlayıcıları:**
  - **Google Gemini** (Gemini 3.8 Flash, 3.7 Flash, 3.6 Flash, 3.1 Pro)
  - **Groq Cloud** (GPT-OSS 120B / 20B, Qwen 3.8, Compound & Compound Mini)
  - **Anthropic Claude** (Claude 3.7 Sonnet, Claude 3.5 Sonnet, Claude 3.5 Haiku)
  - **OpenAI** (GPT-6.1 Sol, GPT-6 Astra, GPT-6 Luna, GPT-4o, GPT-4o Mini)
  - **DeepSeek** (DeepSeek Chat V3, DeepSeek Reasoner R1)
  - **Mistral AI** (Mistral Large, Mistral Small 24B, Codestral, Ministral 8B)
  - **Moonshot Kimi & Alibaba Qwen** (Moonshot v1 128k, Qwen Plus, Qwen Max, Qwen Turbo)
- **📥 Yerel AI Model Hub (Ollama Entegrasyonu):**
  - İnternetsiz ve API kotası harcamadan yerel modelleri tek tıkla indirip çalıştırma merkezi:
    - **Qwen 2.5 3B:** Ultra hafif, harici GPU gerektirmez (Sadece CPU & 4 GB RAM uyumlu).
    - **Qwen 2.5 7B (⭐ Önerilen):** En ideal VRAM (4-6 GB) ve çeviri kapasitesi dengesi. Standart oyuncu bilgisayarlarının (GTX 1660 / RTX 3050 / RTX 4050) GPU belleğine tamamen sığar, CPU darboğazı yapmaz; en yüksek kelime akış hızını ve üstün Türkçe gramerini sunar.
    - **DeepSeek R1 8B:** Düşünce zincirli akıl yürütme (6-8 GB VRAM); deyimler ve felsefi oyun diyalogları için derin analiz.
    - **Qwen 2.5 14B:** Ağır RPG ve roman seviyesinde edebi derinlik (8-12 GB VRAM).
    - **Qwen 2.5 32B:** Amiral gemisi insan seviyesi yerel kapasite (16 GB+ VRAM).
- **🧠 Düşünme & Akıl Yürütme Modu (Reasoning Effort):**
  - Edebi diyaloglar ve deyim analizleri için Low / Medium / High düşünme derinliği ayarı.

### 3. 🛡️ Akıllı Kod & Değişken Maskeleme (Masking Engine)
- Oyun diyaloglarındaki format kodlarını (`{0}`, `%s`, `{PlayerName}`, `\n`, `\t`, `<color>`, `</color>`, `[b]`, `[/b]`) regex tabanlı geçici belirteçlerle (`[VAR_0]`, `[TAG_0]`) korur; çeviri sonrasında bayt hassasiyetiyle eski yerine yerleştirir.
- Oyunların çökmesini (crash) ve değişken bozulmalarını %100 engeller.

### 4. 🌐 Çok Dilli Master Sistem Promptu & Dil Ayarları
- **Desteklenen Diller:** **Türkçe**, **English (İngilizce)**, **Русский (Rusça)** ve **简体中文 (Çince)**.
- Her hedef dil için özel karakter analizi, argo/sokak dili, soylu/saray hitapları ve eşya/yetenek tutarlılığı kurallarını içeren hazır şablonlar.
- Kullanıcılar prompt şablonunu dilediği dilde yükleyebilir ve arayüz üzerinden özgürce düzenleyebilir.

### 5. 🗂️ Oyun Motoru & Arşiv Desteği
- **Unreal Engine:** `.pak` (V1 - V11+) ve `.locres` (V1, V2, V3) ikili lokalizasyon tabloları.
- **Larian Studios (Divinity / Baldur's Gate):** `.pak` (LSPak V7 - V18) ve XML diyalog ağaçları.
- **Evrensel Formatlar:** `.json`, `.csv`, `.tsv`, `.po` ve düz metin dosyaları.
- **Orijinal Dosya Koruması:** Arşiv içerisindeki fontlar, sesler ve dokunulmayan ikili varlıklar bayt bayt korunarak yama paketi üretilir.

### 6. 📊 Kalite Kontrol (QA) & İstatistik Paneli
- **Eksik Değişken & Etiket Taraması (F7):** Unutulan değişkenleri tespit eder ve tek tıkla otomatik onarır.
- **UI Taşma Riski Analizi:** Orijinalinden %40 daha uzun metinleri işaretleyerek arayüz taşmalarını önler.
- **KPI Metrikleri:** Toplam satır, kelime ve karakter hacmi, tamamlanma yüzdesi, canlı çeviri hızı (satır/sn) ve tahmini kalan süre.

---

## 🏗️ Kullanılan Ajan Becerileri & Mimarileri (Agent Skills)

| Skill | Açıklama |
| :--- | :--- |
| **`avalonia-fluent-architect`** | Avalonia UI 11.2+, CommunityToolkit.Mvvm, Windows 11 Fluent 2 / WinUI 3 sekmeli (`TabControl`) ve akordeon kart (`Expander`) tasarımı, Mica/Acrylic teması ve bellek sızıntısız asenkron UI yönetimi. |
| **`dotnet-desktop-singlefile-packaging`** | .NET 8 SDK kullanılarak harici bağımlılık gerektirmeyen, sıkıştırılmış, yerel kitaplıkları gömülü tek parça Windows masaüstü (`GLS.exe`) ve Linux (`GLS`) derleme mimarisi. |
| **`game-ai-localization-orchestrator`** | Çoklu LLM sağlayıcılarında eşzamanlı toplu iş (batch) çevirisi, exponential backoff, rate-limit yönetimi, Translation Memory (TM) önbelleği ve edebi oyun diyalog adaptasyon kuralları. |
| **`game-pak-reverse-engineering`** | Unreal Engine ve Larian arşiv ikili yapısı analizi, AES-256 şifre çözümü, Oodle/LZ4/Zstd sıkıştırma algoritmaları. |
| **`locres-localization-parser`** | Unreal Engine ikili `.locres` formatı (V1-V3), namespace/hash üretimi ve CSV/JSON evrensel lokalizasyon ayrıştırma motoru. |

---

## 🔒 Güvenlik & Gizlilik (Security & Secrets)

- **Sıfır Hardcoded Secret:** Proje kod tabanında, git geçmişinde veya kaynak dosyalarında hiçbir API anahtarı veya şifre yer almaz.
- **İzole Ayar Depolama:** Kullanıcının girdiği tüm API anahtarları yalnızca kullanıcının yerel oturumunda (`%APPDATA%\LocalizePak\settings.json` veya Linux'ta `~/.config/LocalizePak/settings.json`) saklanır.
- `.gitignore` dosyası yerel ayarları, gizli dosyaları ve derleme kalıntılarını tam kapsamlı şekilde izole eder.

---

## 🚀 Kurulum ve Çalıştırma

### 1. Hazır Sürümü İndirme (Tavsiye Edilen)
Herhangi bir .NET kurulumu yapmadan doğrudan kullanmak için [Releases](https://github.com/ulgenzade/GameLocalizationSystem/releases/latest) sayfasından işletim sisteminize uygun sürümü indirin:

- **Windows:** `GLS.exe` veya `GLS-v1.0.0-win-x64.zip` indirip çift tıklayın.
- **Linux:** `GLS-v1.0.0-linux-x64.tar.gz` dosyasını indirin ve terminalde çalıştırın:
  ```bash
  tar -xzf GLS-v1.0.0-linux-x64.tar.gz
  chmod +x GLS
  ./GLS
  ```

### 2. Kaynak Koddan Derleme
```bash
git clone https://github.com/ulgenzade/GameLocalizationSystem.git
cd GameLocalizationSystem
dotnet build
dotnet test
```

### 3. Tek Dosya Paketleme (Publish)

**Windows (x64) için:**
```powershell
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/GLS
```

**Linux (x64) için:**
```bash
chmod +x scripts/build-linux.sh
./scripts/build-linux.sh
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

---

## 👥 Katkıda Bulunanlar & Özel Teşekkürler

- **[Bayhan Ex Machina](https://github.com/ulgenzade/GameLocalizationSystem)** — Unreal Engine 4/5 tersine mühendislik araştırmaları, AES-256-ECB şifreleme modu analizi, `.locres` ikili hash yapısı ve Poppy Playtime vaka incelemesi konusundaki değerli teknik katkı ve rehberlikleri için özel teşekkür ederiz.

</details>

---

<details>
<summary><b>🇬🇧 English Documentation (Click to expand)</b></summary>
<br>

## 🌟 Key Features

### 1. 🐧 Cross-Platform Support (Windows & Linux)
- **Windows (x64):** Modern Fluent 2 / WinUI 3 tabbed interface with Mica/Acrylic theming on Windows 10 & 11.
- **Linux (x64):** Native X11 & Wayland support on Ubuntu, Debian, Fedora, Arch Linux, and SteamOS (Steam Deck). Zero .NET runtime installation required.
- **Desktop Integration:** Includes `.desktop` launcher and desktop entry file for Linux application menus.

### 2. 🤖 Comprehensive AI & LLM Engine Support
- **All-in-One Gateways:**
  - **OpenRouter (`openrouter.ai`)**: Access to hundreds of frontier models with a single API key.
  - **Together AI (`together.ai`)**: Ultra-fast open-source cluster (Llama 3.3 Turbo, DeepSeek V3/R1, Qwen 2.5 72B).
  - **DeepInfra (`deepinfra.com`)**: Cost-effective, high-quota open-source model hosting.
  - **Perplexity AI (`perplexity.ai`)**: Real-time web-grounded validation using Sonar and Sonar Pro.
- **First-Party AI Providers:**
  - **Google Gemini** (Gemini 3.8 Flash, 3.7 Flash, 3.6 Flash, 3.1 Pro)
  - **Groq Cloud** (GPT-OSS 120B / 20B, Qwen 3.8, Compound & Compound Mini)
  - **Anthropic Claude** (Claude 3.7 Sonnet, Claude 3.5 Sonnet, Claude 3.5 Haiku)
  - **OpenAI** (GPT-6.1 Sol, GPT-6 Astra, GPT-6 Luna, GPT-4o, GPT-4o Mini)
  - **DeepSeek** (DeepSeek Chat V3, DeepSeek Reasoner R1)
  - **Mistral AI** (Mistral Large, Mistral Small 24B, Codestral, Ministral 8B)
  - **Moonshot Kimi & Alibaba Qwen** (Moonshot v1 128k, Qwen Plus, Qwen Max, Qwen Turbo)
- **📥 Local AI Model Hub (Ollama Integration):**
  - Download and run models locally without internet or API fees:
    - **Qwen 2.5 3B:** Ultra-lightweight, CPU-only compatible (4 GB RAM).
    - **Qwen 2.5 7B (⭐ Recommended):** Golden ratio of VRAM efficiency (4-6 GB) and translation fluency. Perfectly fits mainstream gaming GPUs (GTX 1660 / RTX 3050 / RTX 4050) with zero CPU throttling and top-tier Turkish prose.
    - **DeepSeek R1 8B:** Chain-of-thought reasoning (6-8 GB VRAM); excels at archaic idioms and complex dialogue.
    - **Qwen 2.5 14B:** High-tier RPG narrative prose (8-12 GB VRAM).
    - **Qwen 2.5 32B:** Flagship human-grade local capability (16 GB+ VRAM).
- **🧠 Reasoning Effort Configuration:**
  - Fine-tune reasoning depth (Low / Medium / High) for complex game lore, archaic dialects, and intricate riddles.

### 3. 🛡️ Intelligent Tag & Variable Masking Engine
- Automatically masks in-game code variables (`{0}`, `%s`, `{PlayerName}`, `\n`, `\t`, `<color>`, `</color>`, `[b]`, `[/b]`) with temporary tokens (`[VAR_0]`, `[TAG_0]`) before passing strings to LLMs.
- Restores them post-translation with byte-exact precision, preventing game engine crashes and syntax corruption.

### 4. 🌐 Multilingual Master System Prompts & App Localization
- **Supported Languages:** **Turkish**, **English**, **Russian**, and **Simplified Chinese**.
- Curated prompt templates tailored for dialect nuances, street slang, royal decorum, item consistency, and gender markers.
- Prompts can be customized, edited, and saved per project in the settings.

### 5. 🗂️ Game Engine & Archive Support
- **Unreal Engine:** `.pak` (Versions 1 through 11+) and binary `.locres` (V1, V2, V3) string tables.
- **Larian Studios (Divinity / Baldur's Gate):** `.pak` (LSPak V7 through V18) and XML dialogue trees.
- **Universal Formats:** `.json`, `.csv`, `.tsv`, `.po`, and raw plain-text.
- **Asset Integrity:** Preserves fonts, textures, audio, and untouched binary entries byte-for-byte in repacked mod archives.

### 6. 📊 Quality Assurance (QA) & Telemetry
- **Missing Variable & Tag Audit (F7):** Flags missed or altered variable tags and provides 1-click auto-repair.
- **UI Overflow Warning:** Warns if translations expand >40% compared to source strings, preventing UI clipping.
- **Live KPI Metrics:** Real-time speed (lines/sec), total character/word counts, completion percentage, and estimated time to completion (ETC).

---

## 🏗️ Architecture & Agent Skills

| Skill | Description |
| :--- | :--- |
| **`avalonia-fluent-architect`** | Avalonia UI 11.2+, CommunityToolkit.Mvvm, Windows 11 Fluent 2 / WinUI 3 tabbed navigation (`TabControl`), collapsible card sections (`Expander`), Mica/Acrylic theming, and leak-free async dispatching. |
| **`dotnet-desktop-singlefile-packaging`** | .NET 8 SDK single-file self-contained desktop packaging (`GLS.exe` for Windows, `GLS` for Linux) with compressed native assemblies and zero runtime dependencies. |
| **`game-ai-localization-orchestrator`** | Multi-provider LLM concurrency orchestrator, adaptive rate-limiting, exponential backoff, Translation Memory (TM) cache, and literary adaptation rules. |
| **`game-pak-reverse-engineering`** | Unreal Engine & Larian archive binary structure parsers, AES-256 decryption, Oodle/LZ4/Zstd decompression algorithms. |
| **`locres-localization-parser`** | Unreal Engine binary `.locres` serializer/parser (V1-V3), CRC/namespace hash computation, and delimited CSV/JSON string table drivers. |

---

## 🔒 Security & Privacy

- **Zero Hardcoded Secrets:** No API keys, credentials, or private paths exist in the codebase or git history.
- **Isolated User Storage:** All user-entered API credentials are stored securely on the local machine under `%APPDATA%\LocalizePak\settings.json` (Windows) or `~/.config/LocalizePak/settings.json` (Linux).
- `.gitignore` strictly excludes local configuration files, build binaries, and temporary artifacts.

---

## 🚀 Installation & Build

### 1. Download Pre-built Binary (Recommended)
Download the standalone binary directly from the [GitHub Releases](https://github.com/ulgenzade/GameLocalizationSystem/releases/latest) page:

- **For Windows:** Download **`GLS.exe`** or **`GLS-v1.0.0-win-x64.zip`** and run.
- **For Linux:** Download **`GLS-v1.0.0-linux-x64.tar.gz`** and execute:
  ```bash
  tar -xzf GLS-v1.0.0-linux-x64.tar.gz
  chmod +x GLS
  ./GLS
  ```

### 2. Building from Source
```bash
git clone https://github.com/ulgenzade/GameLocalizationSystem.git
cd GameLocalizationSystem
dotnet build
dotnet test
```

### 3. Single-File Packaging

**For Windows (x64):**
```powershell
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist/GLS
```

**For Linux (x64):**
```bash
chmod +x scripts/build-linux.sh
./scripts/build-linux.sh
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

---

## 👥 Contributors & Special Thanks

- **[Bayhan Ex Machina](https://github.com/ulgenzade/GameLocalizationSystem)** — Special thanks for invaluable technical research on Unreal Engine 4/5 reverse engineering, AES-256-ECB encryption analysis, `.locres` binary hash architecture, and the Poppy Playtime case study.

</details>

---

## 👥 Katkıda Bulunanlar / Contributors & Special Thanks

- **[Bayhan Ex Machina](https://github.com/ulgenzade/GameLocalizationSystem)** — Unreal Engine tersine mühendislik araştırmaları, AES-256-ECB şifreleme modu analizi ve `.locres` ikili hash mimarisi teknik katkıları / *Unreal Engine reverse engineering, AES-256-ECB architecture analysis & `.locres` research.*

---

## 📄 Lisans / License
Bu proje [MIT Lisansı](LICENSE) kapsamında açık kaynak olarak lisanslanmıştır.  
*This project is open-source and licensed under the [MIT License](LICENSE).*
