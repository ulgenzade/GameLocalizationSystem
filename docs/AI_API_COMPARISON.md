# 🤖 AI_API_COMPARISON.md — Yapay Zeka Servis Karşılaştırması

Oyun yerelleştirme süreçleri için en uygun AI servislerinin kapsamlı karşılaştırması.

---

## Fiyat Karşılaştırması (2026)

| Yapay Zeka Servisi | Girdi (1M Token) | Çıktı (1M Token) | Bağlam Önbellekleme | Özel Direktif | Değerlendirme |
|---|---|---|---|---|---|
| **Google Gemini 2.5/3.5 Flash** | $0.25–$0.30 | $1.50–$2.50 | ✅ %75–90 indirimli | ✅ Mükemmel | ⭐⭐⭐⭐⭐ **Ana Motor** |
| **OpenAI GPT-4o mini** | $0.15 | $0.60 | ✅ %50 indirimli | ✅ Yüksek | ⭐⭐⭐⭐ Yedek Motor |
| **DeepSeek V3/V4 Flash** | $0.14–$0.28 | $0.28–$0.66 | ✅ Düşük maliyetli | ✅ Güçlü Türkçe | ⭐⭐⭐⭐ Ekonomik |
| **DeepL API** | ~$27.50/1M karakter | N/A | ❌ Yok | ❌ Sınırlı | ⭐ Yüksek Maliyet |

---

## Gemini Flash — Neden Ana Motor?

### Avantajlar
- **1M+ token bağlam penceresi** → Oyunun tüm glosary'si tek promptta
- **Context Caching** → Sabit sistem talimatı ve sözlük bir kez önbelleğe alınır, her batch'te tekrar gönderilmez → maliyet %80 düşer
- **System instruction uyumu** → Direktiflere yüksek sadakat
- Türkçe dil bilgisi kalitesi oyun metinleri için yeterli

### Context Caching Nasıl Çalışır?

```
İlk istek (önbellekleme):
  [Sistem talimatı: 2000 token] ─→ Önbelleğe alındı (24 saat)
  [Glosary JSON: 5000 token]    ─→ Önbelleğe alındı (24 saat)
  [Batch metinler: 3000 token]  ─→ Normal fiyat

Sonraki istekler:
  [Önbellek hit: 7000 token]    ─→ %75 indirimli
  [Batch metinler: 3000 token]  ─→ Normal fiyat

Tasarruf: Her batch için 7000 token × %75 = 5250 token tasarruf
```

---

## Maliyet Hesaplama Örneği

**Senaryo:** 500.000 kelimeli oyun → ~700.000 token (Türkçe çıktı dahil)

| Servis | Tahmini Maliyet |
|---|---|
| Gemini Flash (önbellekli) | ~$1.50–$3.00 |
| GPT-4o mini | ~$3.50–$5.00 |
| DeepSeek Flash | ~$2.00–$4.00 |
| DeepL API | ~$19.25 |

---

## Prompt Mühendisliği En İyi Pratikler

### Sistem Talimatı Şablonu

```
Sen profesyonel bir oyun yerelleştirme uzmanısın.
Görevin: Oyun metinlerini İngilizce'den Türkçe'ye çevirmek.

KURALLAR:
1. [VAR_X] ve [NAME_X] etiketlerini HİÇBİR ZAMAN çevirme, olduğu gibi bırak
2. Oyun jargonunu ve atmosferini koru
3. Üslup: {USER_DIRECTIVE}
4. Çevrilmeyecek özel isimler: {GLOSSARY_JSON}
5. Sadece çeviriyi döndür, açıklama ekleme
```

### Batch Format

```json
{
  "texts": [
    {"id": 0, "text": "Slain [NAME_0] with [VAR_0] damage!"},
    {"id": 1, "text": "Quest completed: [VAR_1]"}
  ]
}
```

### Beklenen Yanıt

```json
{
  "translations": [
    {"id": 0, "text": "[NAME_0] [VAR_0] hasar alarak öldürüldü!"},
    {"id": 1, "text": "Görev tamamlandı: [VAR_1]"}
  ]
}
```

---

## Hangi Servisi Seçmeli?

```
Büyük oyun (500K+ kelime) + düşük bütçe?
  → Gemini Flash (Context Caching ile)

Küçük/orta oyun + hız önceliği?
  → GPT-4o mini

Türkçe dil kalitesi en önemli?
  → DeepSeek V4 Flash

Tamamen offline çalışma gerekiyor?
  → Ollama ile lokal model (yakında)
```
