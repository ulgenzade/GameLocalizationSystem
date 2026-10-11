# 📦 FILE_FORMATS.md — Dosya Formatları Teknik Referans

---

## .pak Arşiv Formatı

### Genel Yapı

```
┌──────────────────────────────────┐
│   Dosya Verileri (sıkıştırılmış) │
│   Entry 0: data block            │
│   Entry 1: data block            │
│   ...                            │
├──────────────────────────────────┤
│   İndeks (Index)                 │
│   - Mount point string           │
│   - Entry metadata dizisi        │
├──────────────────────────────────┤
│   Footer (sabit 221 byte)        │
│   - Magic: 0x5A6F12E1            │
│   - Sürüm (Version)              │
│   - İndeks offset                │
│   - İndeks boyutu                │
│   - İndeks SHA1 hash             │
│   - Şifreleme bayrağı            │
│   - AES IV                       │
└──────────────────────────────────┘
```

### Sürüm Tablosu

| Sürüm | UE Sürümü | Özellikler |
|---|---|---|
| V1 | UE4.0–4.2 | Temel arşiv |
| V2 | UE4.3–4.15 | Sıkıştırma bilgisi |
| V3 | UE4.16–4.19 | Hash desteği |
| V4 | UE4.20+ | Şifreleme indeksi |
| V7 | UE4.22+ | Gelişmiş sıkıştırma |
| V8A/V8B | UE4.24–4.25 | Serileştirme iyileştirmeleri |
| V10 | UE4.26–4.27 | IO Store geçiş |
| V11 | UE5+ | IO Store varsayılan |

### Desteklenen Sıkıştırma Algoritmaları

| Algoritma | DLL / Kütüphane | Not |
|---|---|---|
| Zlib | System.IO.Compression | Dahili .NET |
| Gzip | System.IO.Compression | Dahili .NET |
| LZ4 | K4os.Compression.LZ4 | NuGet |
| Zstd | ZstdSharp | NuGet |
| Oodle | oo2core_9_win64.dll | Oyundan kopyalanır, P/Invoke |

### AES Şifreleme

```
Algoritma:    AES-256-CBC
Anahtar:      256-bit (64 hex karakter)
IV:           Footer'dan okunur
Blok boyutu:  16 byte (PKCS7 padding)
```

---

## .locres (Text Localization Resource) Formatı

### Dosya Yapısı

```
Offset  | Boyut  | Alan
--------|--------|------------------------------------------------------
0       | 16     | Magic GUID: {0E14747567-4A03-FC4B-8F3A-D9E3C4E65BD5}
16      | 1      | Sürüm (Version): 1, 2 veya 3
17      | 4      | İsim sayısı (Name Count) [v2+]
21      | N      | İsim dizisi (Name Array) [v2+]
...     | 4      | Namespace sayısı
...     | N      | Namespace dizisi
```

### Namespace Yapısı

```
[Namespace String (UTF-16LE veya UTF-8)]
[Giriş sayısı: int32]
  Per Entry:
    [Key String]
    [Key Hash: uint32 — CityHash64/CRC32]
    [String Resource ID: FTextKey]
    [Metin Değeri String]
```

### Sürüm Farklılıkları

| Sürüm | String Kodlama | Hash Algoritması |
|---|---|---|
| v1 | UTF-16LE | CRC32 |
| v2 | UTF-8 | CityHash64 |
| v3 | UTF-8 | CityHash64 + Ek metadata |

### Önemli Kural

> ⚠️ Çeviri sırasında **sadece metin değerleri (Value)** değiştirilir.
> Key, Hash ve Namespace alanları asla dokunulmamalıdır.
> Hash yanlış hesaplanırsa oyun metni göstermez!

### CityHash Hesaplama (C#)

```csharp
// Key hash'i yeniden hesaplama gerekli değil — sadece Value değişir
// Ancak LocresService yeniden yazarken doğru hash'i korumalı:
uint hash = Crc32.Compute(Encoding.UTF8.GetBytes(key));
// veya v2/v3 için:
ulong cityHash = CityHash.CityHash64(Encoding.UTF8.GetBytes(key));
```

---

## Değişken Formatları (Oyun Motoru Bağımsız)

Farklı oyunlarda karşılaşılan değişken/format kodları:

| Tür | Örnekler | Maskeleme Regex |
|---|---|---|
| Positional | `{0}`, `{1}`, `{2}` | `\{\d+\}` |
| Named | `{PlayerName}`, `{ItemName}` | `\{[A-Za-z_]\w+\}` |
| printf | `%s`, `%d`, `%f`, `%i` | `%[sdfi]` |
| UE Format | `%ls`, `%u` | `%[lsu]` |
| Escape | `\n`, `\t`, `\r` | `\\[ntr]` |
| Rich Text | `<RichColor r="1" ...>...</>` | `<[^>]+>.*?</>` |
| HTML-like | `<b>`, `<i>`, `<color=#FF0000>` | `</?[a-z][^>]*>` |
| Unity TMPro | `<sprite=X>`, `<size=Y>` | `<[a-z]+=\S+?>` |

---

## Yama Paketi (_P.pak) Üretimi

Orijinal oyun dosyası yerine yalnızca değişen `.locres` dosyasını içeren ek bir `.pak` dosyası oluşturulur.

```
Üretilen dosya: GameName-Turkish_P.pak
Mount point:    ../../../GameName/Content/
İçerik:         GameName/Content/Localization/Game/tr/Game.locres
```

> `_P` soneki Unreal Engine'de "patch" anlamına gelir ve yükleme önceliğini artırır.
> Diğer oyun motorları benzer override mekanizmaları kullanır.
