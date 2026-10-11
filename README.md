# 🎮 GLS — Game Localization System

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia%20UI-11.2%2B-purple.svg)](https://avaloniaui.net/)
[![WinUI 3 Fluent 2](https://img.shields.io/badge/UI-Fluent%202%20Tabs-0078D4.svg)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**GLS (Game Localization System)**, video oyunlarının arşiv dosyalarını (`.pak`, `.locres`, `.json`, `.csv`, `.xml`) doğrudan ayrıştıran, kod değişkenlerini ve format etiketlerini koruyarak gelişmiş yapay zeka modelleri (OpenRouter, Gemini, Together AI, Mistral, Claude, OpenAI, DeepSeek, Ollama) ile edebi seviyede çeviren ve tek tıkla yamalanabilir arşivler üreten profesyonel bir masaüstü oyun yerelleştirme yazılımıdır.

---

## 🌟 Temel Özellikler

### 1. 🤖 Kapsamlı AI & LLM Motor Desteği
- **Hepsi Bir Arada Gateway'ler:** 
  - **OpenRouter (`openrouter.ai`)**: Tek API anahtarıyla yüzlerce modele erişim.
  - **Together AI (`together.ai`)**: Ultra hızlı açık kaynak havuzu (Llama 3.3 Turbo, DeepSeek V3/R1, Qwen 2.5 72B).
  - **DeepInfra (`deepinfra.com`)**: Yüksek kotalı ve ekonomik açık kaynak sunucular.
  - **Perplexity AI (`perplexity.ai`)**: Sonar Pro ve Sonar modelleriyle gerçek zamanlı web doğrulama.
- **Resmi AI Sağlayıcıları:**
  - **Google Gemini** (Gemini 3.8 Flash, 3.7 Flash, 3.1 Pro)
  - **Groq Cloud** (GPT-OSS 120B / 20B, Qwen 3.8, Compound)
  - **Anthropic Claude** (Claude 3.7 Sonnet, Claude 3.5 Sonnet & Haiku)
  - **OpenAI** (GPT-6.1 Sol, GPT-6 Astra, GPT-4o, GPT-4o Mini)
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
| **`dotnet-desktop-singlefile-packaging`** | .NET 8 SDK kullanılarak harici bağımlılık gerektirmeyen, sıkıştırılmış, yerel kitaplıkları gömülü tek parça Windows masaüstü yürütülebilir dosyası (`dist/GLS_SingleFile/GLS.exe`) derleme mimarisi. |
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

### 1. Depoyu Klonlama
```bash
git clone https://github.com/ulgenzade/GameLocalizationSystem.git
cd GameLocalizationSystem
```

### 2. Projeyi Derleme
```bash
dotnet build
```

### 3. Birim Testlerini Çalıştırma
```bash
dotnet test
```
*(Tüm testlerin yeşil geçtiğinden emin olun - 34/34 Test)*

### 4. Tek Dosya (.exe) Paketleme (Publish)
Harici bağımlılık gerektirmeyen bağımsız tek dosya üretmek için:
```powershell
dotnet publish src/LocalizePak.UI/LocalizePak.UI.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  --self-contained true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o dist/GLS_SingleFile
```
Derleme sonrasında `dist/GLS_SingleFile/GLS.exe` (yaklaşık ~48 MB) hazır hale gelir.

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

## 📄 Lisans
Bu proje [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır.
