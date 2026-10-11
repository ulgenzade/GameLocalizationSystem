# 📋 WORKFLOW.md — Kullanım Kılavuzu

Adım adım oyun yerelleştirme süreci.

---

## Genel Akış

```
1. .pak Yükleme
      ↓
2. .locres Seçme & Metin Ayrıştırma
      ↓
3. Glosary & Direktif Tanımlama
      ↓
4. AI ile Çeviri
      ↓
5. Manuel Düzeltme & Onay
      ↓
6. _P.pak Yama Paketi Üretimi
```

---

## Adım 1 — .pak Dosyasını Yükle

1. Uygulamayı aç
2. **"Dosya Seç"** butonuna tıkla
3. Oyunun `.pak` dosyasını seç (örn. `pakchunk0-WindowsNoEditor.pak`)
4. Eğer dosya şifreliyse, **AES Anahtarı** alanına 256-bit Hex anahtarı gir:
   ```
   Örnek: 0x1234ABCD...
   ```
5. **"Aç"** butonuna tıkla — arşiv içindeki dosyalar listelenir

---

## Adım 2 — Yerelleştirme Dosyasını Seç

1. Sol paneldeki dosya ağacından `.locres` uzantılı dosyaları bul
2. İstediğin `.locres` dosyasına çift tıkla
3. Uygulama binary dosyayı ayrıştırır ve sağ paneldeki DataGrid'e yükler:

| Sütun | Açıklama |
|---|---|
| **Namespace** | Oyun içi kod bloğu grubu |
| **Key** | Benzersiz metin anahtarı |
| **Kaynak Metin** | Orijinal oyun metni |
| **Türkçe Çeviri** | AI çevirisi veya manuel giriş |
| **Durum** | Bekliyor / Çevriliyor / Tamamlandı / Hatalı |

---

## Adım 3 — Glosary ve Direktifleri Tanımla

### Özel İsim Glosary'si
- **"Glosary"** paneline geç
- Oyun özel isimlerini ekle:
  ```
  Winterfell → Winterfell (çevirme)
  Shadowmourne → Gölgebane (özel çeviri)
  Cloud Strife → Cloud Strife (koru)
  ```
- Sistem ayrıca metinleri otomatik tarayarak özel isim adaylarını önerir

### Çeviri Direktifi
Alt paneldeki direktif kutusuna üslup talimatı yaz:
```
Oyun orta çağ temalıdır. Üslubu kasvetli ve epik tut.
Asker rütbelerini Türkçe askeri terimlerle eşleştir.
"Lore" terimlerini Türkçeleştirme, orijinal bırak.
```

---

## Adım 4 — AI ile Çeviri Başlat

1. **"Ayarlar"** ekranından AI servisi seç (Gemini / OpenAI / DeepSeek)
2. API anahtarını gir ve kaydet
3. **"Çeviriyi Başlat"** butonuna tıkla
4. İlerleme çubuğu ve anlık durum göstergesi çeviri sürecini takip eder:
   - ✅ Tamamlanan satırlar yeşil
   - ⚠️ Hatalı etiket tespitinde sarı uyarı
   - ❌ Başarısız istekler kırmızı (otomatik yeniden denenir)

### Rate-Limit Yönetimi
Sistem 429 hatası aldığında **Exponential Backoff** ile otomatik yeniden dener:
```
1. deneme başarısız → 2 saniye bekle
2. deneme başarısız → 4 saniye bekle
3. deneme başarısız → 8 saniye bekle
...
```

---

## Adım 5 — Manuel Düzeltme

- DataGrid'de herhangi bir hücreye tıklayarak çeviriyi düzenle
- **"Hatalı"** olarak işaretlenmiş satırları filtrele ve onar
- Toplu seçimle satırları tekrar çevir veya atla

---

## Adım 6 — Yama Paketi Üret

1. **"Dışa Aktar"** butonuna tıkla
2. Çıktı klasörünü seç
3. Uygulama şunları üretir:
   ```
   GameName-Turkish_P.pak
   ```
4. Bu dosyayı oyunun `Paks/` klasörüne at
5. Oyunu başlat — `_P` öncelikli yüklendiği için Türkçe metinler görünür

> ⚠️ Orijinal oyun dosyaları hiçbir zaman değiştirilmez!

---

## Sorun Giderme

| Sorun | Çözüm |
|---|---|
| "Invalid AES Key" hatası | Anahtarın 64 hex karakter olduğunu doğrula |
| Çeviri başlamıyor | API anahtarının geçerli ve kotanın dolu olmadığını kontrol et |
| Hatalı etiket uyarısı | O satırı seç ve "Yeniden Çevir" butonunu kullan |
| .pak açılmıyor | Oyunun IO Store (.utoc/.ucas) formatını kullandığı olabilir |
