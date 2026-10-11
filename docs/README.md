# 🎮 GameLocalizationSystem

**Herhangi bir oyunun `.pak` arşiv dosyalarından metinleri çıkaran, yapay zeka ile Türkçeye çeviren ve tekrar paketleyen çapraz platform masaüstü uygulaması.**

> Unreal Engine ile sınırlı değil — .pak formatını kullanan her oyunla çalışır.

---

## ✨ Özellikler

| Özellik | Açıklama |
|---|---|
| 📦 .pak Desteği | UE4/UE5 ve genel .pak arşivlerini okur/yazar |
| 🔐 AES Şifreleme | 256-bit Hex AES anahtarı ile şifrelenmiş arşivleri açar |
| 📝 .locres Ayrıştırma | Binary lokalizasyon dosyalarını okur ve yazar (v1-v3) |
| 🤖 Çoklu AI Motor | Gemini Flash, GPT-4o mini, DeepSeek V3/V4 desteği |
| 🛡️ Değişken Koruma | `{0}`, `{PlayerName}`, `%s`, `<tag>` gibi oyun değişkenlerini korur |
| 📚 Özel İsim Yönetimi | Karakter, yer, silah isimlerinin yanlış çevrilmesini engeller |
| ⚡ Batch Çeviri | Eşzamanlı API istekleri ile hızlı toplu çeviri |
| 💾 Yama Paketi Üretimi | Orijinal oyun dosyalarına dokunmadan `_Turkish_P.pak` üretir |

---

## 🚀 Hızlı Başlangıç

### Gereksinimler
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) veya üzeri
- Windows 10+, macOS 12+, veya Linux (Ubuntu 20.04+)

### Kurulum

```bash
git clone https://github.com/kullanici/GameLocalizationSystem.git
cd GameLocalizationSystem/src
dotnet restore
dotnet build
dotnet run --project LocalizePak.UI
```

---

## 📁 Proje Yapısı

```
GameLocalizationSystem/
├── docs/                          ← Dokümantasyon
│   ├── README.md                  ← Bu dosya
│   ├── ARCHITECTURE.md            ← Yazılım mimarisi
│   ├── WORKFLOW.md                ← Kullanım kılavuzu
│   ├── CONTRIBUTING.md            ← Katkı rehberi
│   ├── AI_API_COMPARISON.md       ← AI servis karşılaştırması
│   ├── FILE_FORMATS.md            ← .pak/.locres format detayları
│   ├── TECH_STACK.md              ← Teknoloji analizi
│   └── master_prompt.txt          ← AI kodlama asistanı promptu
└── src/
    ├── LocalizePak.sln
    ├── LocalizePak.Core/          ← Domain modelleri
    ├── LocalizePak.IO/            ← Dosya okuma/yazma
    ├── LocalizePak.Translation/   ← AI çeviri motorları
    └── LocalizePak.UI/            ← Avalonia arayüzü
```

---

## 🤖 Desteklenen AI Servisleri

| Servis | Model | Bağlam Önbellekleme |
|---|---|---|
| Google Gemini | 2.5/3.5 Flash | ✅ (%75-90 indirimli) |
| OpenAI | GPT-4o mini | ✅ (%50 indirimli) |
| DeepSeek | V3 / V4 Flash | ✅ |

---

## 📄 Lisans

MIT License — Detaylar için bkz. [LICENSE](../LICENSE)
