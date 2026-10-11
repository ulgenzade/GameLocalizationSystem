# 🏗️ ARCHITECTURE.md — Yazılım Mimarisi

GameLocalizationSystem, **4 katmanlı (layered) mimari** ile geliştirilmiştir. Her katman bağımsız test edilebilir ve değiştirilebilir.

---

## Katman Diyagramı

```
┌─────────────────────────────────────────────────────────┐
│                   LocalizePak.UI                        │
│        (Avalonia UI — MVVM — CommunityToolkit.Mvvm)     │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│               LocalizePak.Translation                   │
│   (AI API Entegrasyonları, Maskeleme, Batch Yönetimi)   │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│                  LocalizePak.IO                         │
│       (.pak Okuma/Yazma, .locres Binary Ayrıştırma)     │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│                 LocalizePak.Core                        │
│     (Domain Modelleri, Arayüzler, Sabitler, Enum'lar)   │
└─────────────────────────────────────────────────────────┘
```

---

## 1. LocalizePak.Core

**Sorumluluk:** Tüm katmanların bağımlı olduğu temel domain modelleri ve soyut arayüzler.

### Temel Sınıflar

| Sınıf / Arayüz | Açıklama |
|---|---|
| `PakArchive` | .pak dosyası domain modeli (yol, şifreleme, giriş listesi) |
| `PakEntry` | Arşiv içindeki tek dosya girdisi |
| `LocresFile` | .locres dosyası modeli (namespace koleksiyonu) |
| `LocresNamespace` | İsim alanı → anahtar-değer çifti koleksiyonu |
| `LocalizationEntry` | Tek çeviri birimi (Key, Source, Target, Status) |
| `ITranslationProvider` | AI servis soyut arayüzü |
| `IPakReader` | .pak okuma arayüzü |
| `IPakWriter` | .pak yazma arayüzü |
| `ILocresSerializer` | .locres serileştirme arayüzü |
| `TranslationStatus` | Enum: Pending / Translating / Done / Error / Skipped |
| `AppSettings` | API anahtarları ve kullanıcı tercihleri |

---

## 2. LocalizePak.IO

**Sorumluluk:** Fiziksel dosya okuma/yazma işlemleri. Tüm binary I/O buradadır.

### Temel Sınıflar

| Sınıf | Açıklama |
|---|---|
| `PakService` | .pak indeksi okuma, AES-256 deşifreleme, .locres çıkarma |
| `LocresService` | .locres binary ayrıştırma ve serileştirme (v1-v3) |
| `FileFormatDetector` | Dosya magic bytes ile format otomatik tespiti |
| `AesDecryptionHelper` | Bellek içi (in-memory) AES şifre çözme yardımcısı |
| `OodleHelper` | Oodle (oo2core) dinamik yükleme köprüsü (P/Invoke) |

### .pak Okuma Akışı

```
PakService.OpenAsync(path, aesKey?)
    └── Footer okuma (son 221 byte)
        └── Index okuma + AES deşifreleme
            └── Giriş listesi (Entry[]) çıkarma
                └── .locres dosyaları filtreleme
                    └── MemoryStream olarak dön
```

---

## 3. LocalizePak.Translation

**Sorumluluk:** AI API entegrasyonları, değişken koruma ve çeviri orkestrasyon mantığı.

### Temel Sınıflar

| Sınıf | Açıklama |
|---|---|
| `MaskingEngine` | Değişken ve özel isim maskeleme/geri yükleme |
| `GeminiTranslationProvider` | Google Gemini Flash API entegrasyonu |
| `OpenAiTranslationProvider` | OpenAI GPT-4o mini API entegrasyonu |
| `DeepSeekTranslationProvider` | DeepSeek V3/V4 API entegrasyonu |
| `BatchTranslationOrchestrator` | Toplu çeviri yönetimi, rate-limit, retry |
| `GlossaryManager` | Özel isim sözlüğü yönetimi |
| `TranslationValidator` | Çeviri sonrası değişken sayısı ve etiket doğrulama |

### Maskeleme Akışı

```
Giriş metni:
  "Slain {PlayerName} with {0} damage!\n"

MaskingEngine.Mask():
  "Slain [NAME_0] with [VAR_0] damage![NL_0]"

AI Çevirisi:
  "[NAME_0] {0} hasar vererek öldürüldü![NL_0]"

MaskingEngine.Restore():
  "{PlayerName} {0} hasar vererek öldürüldü!\n"
```

---

## 4. LocalizePak.UI

**Sorumluluk:** Avalonia UI ile MVVM deseni uygulayan kullanıcı arayüzü.

### Temel Bileşenler

| Bileşen | Açıklama |
|---|---|
| `MainWindow.axaml` | Ana pencere düzeni (sol panel + sağ editör) |
| `MainViewModel.cs` | Merkezi ViewModel (IAsyncRelayCommand'lar) |
| `TranslationEditorView.axaml` | Sanallaştırılmış DataGrid çeviri editörü |
| `TranslationEditorViewModel.cs` | Editör ViewModel |
| `SettingsView.axaml` | API anahtarı ve tercih ayarları |
| `SettingsViewModel.cs` | Ayarlar ViewModel |
| `GlossaryView.axaml` | Özel isim sözlük yönetimi |

---

## Bağımlılık Grafiği

```
UI → Translation → Core
UI → IO → Core
Translation → Core
IO → Core
```

> Her katman yalnızca kendinden **alt** katmanlara bağımlıdır.
