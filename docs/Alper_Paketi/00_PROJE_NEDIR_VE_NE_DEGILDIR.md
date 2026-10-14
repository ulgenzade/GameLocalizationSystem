# 🎮 GameLocalizationSystem (GLS) — Proje Nedir ve Ne Değildir?

> **Hazırlayan:** Atilla Toksöz  
> **Hedef:** Alper ve Claude Geliştirme Süreci İçin Oryantasyon Dokümanı  
> **Tarih:** Ekim 2026  

---

## 🧭 1. Giriş ve Vizyon

**GameLocalizationSystem (GLS)**; modern video oyunlarının (Unreal Engine 4 & 5, Larian Studios Divinity/Baldur's Gate 3, Godot, ID Tech, CryEngine, Unity vb.) arşivlerini açıp kapatabilen, şifreli arşivlerin AES anahtarlarını statik disassembly ve canlı RAM analiziyle saniyeler içinde çözen, metin tablolarını ikili (binary) hash bütünlüğünü bozmadan güncelleyen ve çoklu Büyük Dil Modelleri (LLM) ile endüstriyel kalitede Türkçe yerelleştirme sağlayan **masaüstü CAT (Computer-Assisted Translation) ve Tersine Mühendislik (Reverse Engineering) platformudur**.

---

## ❌ 2. Proje Ne DEĞİLDİR?

GLS'nin ne olduğunu anlamak için önce ne **olmadığını** netleştirmek gerekir:

1. **Sadece basit bir Google Translate / DeepL sarmalayıcısı (wrapper) DEĞİLDİR:**
   - Düz bir çeviri aracına oyun dosyası verilemez; çünkü oyun dosyaları düz metin değil, ikili ofsetler, UTF-16 bayt uzunlukları, CityHash/CRC32 hash tabloları ve LZ4/Zstd/Oodle sıkıştırmalı arşivlerdir.
2. **Sıradan bir metin editörü (Notepad++ / Excel) DEĞİLDİR:**
   - Bir `.locres`, `.uasset` veya `.pak` dosyasını Excel ile açıp kaydederseniz oyun anında çöker. GLS, ikili dosya yapısını bayt düzeyinde koruyan özel codec'lere sahiptir.
3. **Korsan oyun dağıtım aracı DEĞİLDİR:**
   - Sistem, 5846 sayılı FSEK (Fikir ve Sanat Eserleri Kanunu) hükümlerine %100 uyar. Oyunun orijinal telifli dosyalarını asla dağıtmaz; sadece kullanıcının orijinal oyununa eklenen `_P.pak` yama dosyalarını üretir.
4. **Bağlamsız (blind) çeviri aracı DEĞİLDİR:**
   - Oyun içi diyalog etiketlerini (`[DWARF]`, `{PlayerName}`, `%s`, `\C[1]`, `<RichColor>`) AI modellerinin bozmasını engelleyen askeri düzeyde koruma kalkanına (`MaskingEngine`) sahiptir.

---

## ✅ 3. Proje NEDİR?

GLS, oyun yerelleştirme sektöründeki en büyük 4 problemi aynı çatı altında çözen bütünleşik bir sistemdir:

### 💎 A. İkili Arşiv ve Tersine Mühendislik Motoru (`LocalizePak.IO`)
- **Unreal Engine (.pak):** CUE4Parse ve yerli `UnrealCuePakHandler` ile Unreal 4.18'den Unreal 5.4+'a kadar tüm arşivleri otomatik sürüm algılama (`EGame`) ile açar.
- **Yama Paketi Üreticisi (`UnrealPakWriter`):** Oyunu baştan paketlemeden, 221 baytlık standart footer ve ECB şifreleme ile doğrudan Unreal Engine'in tanıdığı `*_P.pak` yama dosyalarını sıfırdan oluşturur.
- **Locres Dual GUID Motoru (`LocresCodec`):** UE'nin V0-V3 CityHash64/CRC32 ikili yerelleştirme tablolarını ofset ve anahtar hash'lerini bozmadan sadece metinleri güncelleyerek yeniden derler.
- **Gömülü Metin Yamalayıcı (`UAssetStringPatcher`):** `.locres` açılmamış oyunlarda `.uasset` / `.uexp` içindeki FString dizelerini tarar, yeni Türkçe metin uzadığında veya kısaldığında Pascal Little Endian Length baytını dinamik olarak yeniden hesaplayarak ikili yapıyı korur.
- **Diğer Motorlar:** Larian Studios (`LSPakHandler` V7-V18), Godot (`GodotPakHandler` GDPC V1/V2), Quake/GoldSrc (`ClassicPakHandler`), CryEngine (`ZipPakHandler`) ve Gettext (`PoCodec`).

### 🔑 B. Çevrimdışı ve Canlı Bellek Şifre Çözücü
- **Static AES Scanner (`StaticAesKeyScanner`):** Oyunu hiç çalıştırmadan, oyunun `.exe` dosyasını 0.4 saniyede x86-64 assembly desenleriyle tarayarak AES-256 anahtarını çıkarır.
- **Pak Index Tester (`PakIndexKeyTester`):** Bulunan anahtarı footer mount point ofsetinde mikrosaniyede doğrular.
- **Key Vault (`AesKeyVaultService`):** 30+ AAA oyunun gömülü anahtarlarını barındırır, GitHub/FModel bulut senkronizasyonu yapar ve dosya yolundan anahtarı otomatik eşleştirir.
- **RAM AES Extractor (`RamAesExtractor`):** Çalışan oyun işlem belleğinden (VirtualQueryEx / ReadProcessMemory) aktif anahtarı yakalar.

### 🧠 C. Yapay Zeka Çeviri ve Optimizasyon Zekası (`LocalizePak.Translation`)
- **Çoklu Sağlayıcı:** Gemini 2.5 Flash, Claude 3.5/3.7 Sonnet, OpenAI GPT-4o, DeepSeek V3, Groq (Llama 3.3 70B), Mistral, Together AI, Perplexity ve yerel **Ollama Hub**.
- **Deduplication Engine (Token Tasarrufu):** "Found 1 gold", "Found 500 gold" gibi dinamik satırları `Found {NUM} gold` şablonuna indirgeyerek tek bir AI isteğinde çözer; ardından dinamik sayıları geri yükler. **%40 - %70 token tasarrufu sağlar.**
- **Masking Engine:** Oyun kodlarını (`{0}`, `%s`, `[var]`, `\C[1]`, `<RichColor>`) AI'dan gizler ve çeviri sonrasında milimetrik olarak geri yerleştirir.
- **Pseudolocalization Engine:** 0 token harcayarak metinleri %40 uzatıp aksan ekler (`[~!! ... !!~]`); arayüz taşmalarını test ettirir.
- **LQA & MQM/DQF Denetçisi:** Uluslararası çeviri kalite standardında Accuracy, Fluency, Terminology ve Design kontrolleri yapar; buton taşma risklerini erkenden uyarır.

### 🖥️ D. Windows 11 Fluent 2 Arayüzü (`LocalizePak.UI`)
- Avalonia UI (v11+) ve CommunityToolkit.Mvvm ile inşa edilmiştir.
- Mica / Acrylic efektleri, koyu tema, sanallaştırılmış yüksek performanslı DataGrid.
- Çok dilli sekme mimarisi (`Game.locres` vs `Engine.locres` motor ayrımı).
- F5 (Tek Tıkla Başla), F6 (Çevrilenleri Doğrudan Kaydet), Esc (Durdur) akıllı eylem çubuğu.

---

## 🏛️ 4. Proje Mimarisi ve Katmanlar

```
GameLocalizationSystem/
│
├── src/LocalizePak.Core/           ──> Domain Modelleri, Enumlar, Arayüzler
├── src/LocalizePak.IO/             ──> Arşiv Ayrıştırıcılar, AES Araçları, Hex Patcher, Font Inspector
├── src/LocalizePak.Translation/    ──> AI Sağlayıcıları, Deduplication, Masking, LQA MQM/DQF
├── src/LocalizePak.UI/             ──> Avalonia UI 11 Arayüzü, MVVM Modülleri, DataGrid
├── src/LocalizePak.Cli/            ──> Otomasyon ve Konsol Komut Satırı Arayüzü
└── src/tests/LocalizePak.Tests/    ──> 116 Adet Kapsamlı xUnit Birim Testi (%100 Yeşil)
```

---

## ⚖️ 5. Hukuki Güvence (5846 Sayılı FSEK)

Proje içindeki `PatchInstallerGenerator.cs` modülü, üretilen yamaları Türk Hukuku'na uygun olarak dağıtmak için tek tıkla kurulum sihirbazı (`Kurulum_ve_Yedekle.bat`) ve geri yükleme kaldırıcı (`Yamayi_Kaldir_Orijinale_Don.bat`) üretir. Kullanıcıların telifli oyun dosyalarını dağıtması engellenir; yalnızca Türkçe yerelleştirme deltası dağıtılır.
