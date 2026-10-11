---
name: locres-localization-parser
description: >-
  Parses, serializes, and inspects video game localization formats including Unreal Engine
  binary .locres (V1, V2, V3), Larian dialog XML/LSX, JSON, and delimited CSV/TSV tables.
  Use when reading or writing localization string tables, computing key hashes, or handling
  special variable masking tags in game dialogue strings.
---

# Game Localization & Binary .locres Serializer Guide

Bu rehber, Unreal Engine ikili `.locres` dosyaları ile XML, LSX, JSON ve CSV formatlarındaki oyun diyalog tablolarını işlemek, ayrıştırmak ve serileştirmek için kullanılır.

## 📜 Unreal Engine `.locres` İkili Formatı

1. **GUID Doğrulama:**
   - Dosya ilk 16 baytında şu sabit GUID dizisini içerir:
     `{0E147475-674A-03FC-4B8F-3AD9E3C4E65B}` (`0x0E, 0x14, 0x74, 0x75, ...`)
   - 17. bayt dosya sürümüdür (`LocresVersion`: V1 = 0, V2 = 1, V3 = 2).

2. **Dize Kodlaması (FString Yapısı):**
   - Pozitif int32 uzunluk: UTF-8 dizesi (sonunda `\0` null sonlandırıcı bulunur).
   - Negatif int32 uzunluk: UTF-16LE dizesi (`charCount = Math.Abs(length)`).
   - Güvenlik: `int.MinValue` mutlak değer taşması koruması ve azami dize boyutu tavanı (100 MB) uygulanmalıdır.

3. **Hiyerarşik Yapı:**
   - `LocresFile` -> Birden fazla `LocresNamespace` (örn: `"Dialogs"`, `"Quests"`, `"UI"`) -> Her namespace altında `LocalizationEntry` listesi (Key, KeyHash, SourceText, TranslatedText).

## 🛡️ Değişken ve Kod Maskeleme (Masking Engine)

Oyun metinleri zengin metin etiketleri (`<color>`, `<b>`), programlama değişkenleri (`{0}`, `{PlayerName}`) ve printf biçimlendiricileri (`%s`, `%d`) içerir. Yapay zeka bu etiketleri bozmamalıdır:

1. **Maskeleme Kuralları ve Sıralama:**
   - `{PlayerName}`, `{Item}` -> `[VAR_X]`
   - `{0}`, `{1}` -> `[POS_X]`
   - `[DWARF]`, `[SOLDIER]` (Diyalog koşulları) -> `[GTAG_X]`
   - `%s`, `%d`, `%f` -> `[FMT_X]`
   - `\n`, `\t`, `\r` -> `[ESC_X]`
   - `</>`, `</color>`, `</b>` -> `[ETAG_X]` (Açılışlardan önce maskelenir)
   - `<color=#FF0000>`, `<RichColor ...>` -> `[TAG_X]`
   - Özel terimler / Sözlük (Glossary) -> `[NAME_X]`

2. **Otomatik Onarım (RestoreAndRepair):**
   - Yapay zekadan dönen yanıtta etiketler büyük/küçük harf duyarsız (`[var_0]` / `[ VAR_0 ]`) regex ile orijinal yerine konur.
   - Eksik kalan hayati oyun değişkenleri metnin sonuna otomatik eklenerek oyunun çökmesi (%100) engellenir.
