---
name: game-ai-localization-orchestrator
description: >-
  Orchestrates concurrent batch game localization using multi-LLM providers (Gemini, Claude,
  OpenAI, DeepSeek, Groq, Ollama) with robust rate-limit handling, exponential backoff,
  translation memory caching, and specialized Turkish literary/game adaptation rules.
  Use when tuning translation prompts, handling API 429 rate limits, persona adaptation, or optimizing batch throughput.
---

# Game AI Localization Orchestrator & Turkish Adaptation Guide

Bu rehber, video oyunu yerelleştirme süreçlerinde çoklu LLM modellerini koordine etmek, API hız sınırlarını (Rate Limit / 429) aşmak ve edebi, doğal Türkçe oyun çevirileri elde etmek için kullanılır.

## 🤖 Çoklu LLM Sağlayıcı Mimarisi

1. **Desteklenen Sağlayıcılar:**
   - **Groq:** GPT-OSS 120B, GPT-OSS 20B, Qwen 3.8 27B, Compound (Ultra yüksek hız, düşük gecikme).
   - **Google Gemini:** gemini-3.8-flash, gemini-3.7-flash, gemini-3.6-flash, gemini-3.5-flash-lite, gemini-3.1-pro.
   - **Anthropic Claude:** Claude Sonnet 5.5, Claude Haiku 5.5, Claude Opus 5.5, Claude 3.7 Sonnet (Üst düzey edebi kalite).
   - **OpenAI:** GPT-6 Astra, GPT-6.1 Sol, GPT-6 Luna, GPT-5.6 Terra, GPT-4o.
   - **DeepSeek:** deepseek-chat (V3), deepseek-reasoner (R1) (Maliyet ve akıl yürütme avantajı).
   - **Özel / Yerel LLM:** Ollama, LM Studio, vLLM (Qwen 3, Llama 3.3, DeepSeek R1).

2. **HTTP İstek Güvenliği & Header İzolasyonu:**
   - `HttpClient.DefaultRequestHeaders` asla değiştirilmemelidir.
   - Her istek için izole `request.Headers.Authorization` veya `x-goog-api-key` başlığı kullanılmalıdır.
   - İstisna mesajlarında API anahtarları asla düz metin loglanmamalıdır.

## 🔁 Kota & Hız Sınırı Yönetimi (Exponential Backoff)

- HTTP 429 (Too Many Requests) veya 503 (Service Unavailable) durumlarında üstel bekleme uygulanır:
  ```text
  Deneme 1: 2 saniye
  Deneme 2: 4 saniye
  Deneme 3: 8 saniye
  Deneme 4: 16 saniye
  Deneme 5: 32 saniye
  ```
- Gemini Free Tier günlük istek kotası (RESOURCE_EXHAUSTED) alındığında kullanıcıya alternatif model (Groq / DeepSeek / Ollama) önerilmelidir.

## 🎭 Türkçe Edebi ve Oyun Yerelleştirme Kuralları

Türkçe hedef dilde mekanik kelime-kelime çeviri (dublaj Türkçesi) yerine yaşayan sokak ve sahne Türkçesi üretilmelidir:

1. **Gizli Özne & Pro-Drop:**
   - Cümle başlarında gereksiz "Ben", "Sen", "O" zamirleri elenir (*"Ben kaleye gidiyorum"* yerine *"Kaleye gidiyorum"*).
2. **Karakter & Persona Uyarlaması:**
   - **Goblin / Yaratık (Kırık konuşma):** *"Ben gitmek şimdi, patron kızgın!"*
   - **Soylu / Saraylı:** *"Varlığınız fakirhanemizi şereflendirdi, lordum."*
   - **Haydut / Sokak Serserisi:** *"Bi' halt bildiğin yok, toz ol buradan!"*
3. **Mizah ve Deyimler:**
   - İngilizce deyimler motamot çevrilmez; Türk kültür ve oyun dilindeki doğal karşılığı kullanılır (*"piece of cake"* -> *"çocuk oyuncağı"*).
4. **Çeviri Belleği (Translation Memory - TM):**
   - Daha önce çevrilmiş aynı satırlar diskteki `translation_memory.json` üzerinden `0 Kota` ve `0 Maliyet` ile anında doldurulur.
