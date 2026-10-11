---
name: game-pak-reverse-engineering
description: >-
  Analyzes, unpacks, decrypts, and repacks binary video game archives including
  Unreal Engine (.pak V1-V11+), Larian Studios LSPak (V7-V18), CryEngine/ZIP, and Quake PACK.
  Use when dealing with game archive binary structures, AES-256 keys, Oodle/LZ4/Zstd compression,
  or extracting encryption keys from live game process RAM.
---

# Game Archive Reverse Engineering & Binary Decryption Guide

Bu rehber, farklı oyun motorlarına ait ikili (binary) `.pak` arşiv formatlarının ayrıştırılması, sıkıştırmalarının açılması, AES-256 şifrelerinin çözülmesi ve yeniden paketlenmesi için kullanılır.

## 📦 Desteklenen Arşiv Formatları ve İmzalar (Magic)

| Motor / Format | Sihirli İmza (Magic) | Ofset Konumu | Sıkıştırma Yöntemleri |
|---|---|---|---|
| **Unreal Engine (.pak)** | `0x5A6F12E1` | Footer (Dosya sonu, -44 bayt veya son 8 KB tarama) | Zlib, Gzip, Oodle, LZ4, Zstd |
| **Larian LSPak (BG3/DOS1)** | `0x4B50534C` ("LSPK") | Header (Dosya başı, ofset 0) | LZ4, Zlib, Ham (None) |
| **Larian LSPak (DOS2 V13)** | `0x4B50534C` ("LSPK") | Trailer (Dosya sonu, -4 bayt ve -40 bayt başlık) | LZ4, Zlib, Ham (None) |
| **CryEngine / Godot (ZIP)** | `0x04034B50` ("PK\x03\x04") | Header (Dosya başı, ofset 0) | Deflate, Zlib, Store |
| **Quake / GoldSrc (PACK)** | `0x4B434150` ("PACK") | Header (Dosya başı, ofset 0) | Ham (Sıkıştırmasız) |

## 🔑 AES-256 Şifre Çözme Mimarisi (Unreal Engine)

1. **Unreal Pak Footer ve Index Ayrıştırma:**
   - Unreal paketlerinde dosya sonundaki Footer alanı genelde **şifresizdir** (düz metin).
   - Şifrelenen alan, Footer içindeki `IndexOffset` ve `IndexSize` ile işaret edilen **Index (Dosya Listesi)** tablosudur.
   - Doğrulama adımı: Şifresi çözülen index bloğunun ilk 4 baytı `mountLen` (int32) değeridir ve takip eden karakterler geçerli ASCII mount point'tir (örn: `../../../Game/Content/`).

2. **RAM Bellek Tarama (Live Process Extraction):**
   - Windows API (`OpenProcess`, `VirtualQueryEx`, `ReadProcessMemory`) kullanılır.
   - Yalnızca `MEM_COMMIT` durumundaki ve `PAGE_READONLY | PAGE_READWRITE` korumalı bellek bölgeleri taranır.
   - 32 baytlık (256-bit) yüksek entropili bellek blokları aday gösterilir.
   - Kesin doğrulama için aday anahtar doğrudan paketin `IndexOffset` bloğu üzerinde test edilir.

## 🗜️ Sıkıştırma Algoritmaları ve P/Invoke

1. **Oodle (`oo2core_9_win64.dll`):**
   - Unreal Engine 4.20+ ve UE 5 oyunlarının büyük kısmı Oodle (Kraken, Leviathan, Mermaid, Selkie) kullanır.
   - P/Invoke sırasında güvenlik için DLL daima `AppDomain.CurrentDomain.BaseDirectory` üzerinden tam yol ile yüklenmelidir (`NativeLibrary.Load`).
2. **LZ4 & Zstandard:**
   - Larian LSPak ve modern motorlar için `K4os.Compression.LZ4` ve `ZstdSharp` yönetilen kütüphaneleri kullanılır.
