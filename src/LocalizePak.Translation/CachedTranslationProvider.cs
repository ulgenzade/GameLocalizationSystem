using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;

namespace LocalizePak.Translation
{
    /// <summary>
    /// Decorator for ITranslationProvider that caches translations using TranslationMemory.
    /// </summary>
    public sealed class CachedTranslationProvider : ITranslationProvider
    {
        private readonly ITranslationProvider _inner;
        private readonly TranslationMemory _memory;

        public CachedTranslationProvider(ITranslationProvider inner, TranslationMemory memory)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        }

        public string Name => _inner.Name + " (Cached)";

        public bool IsConfigured => _inner.IsConfigured;

        public void Configure(string apiKey, string model = "")
        {
            if (_inner is Providers.GeminiTranslationProvider gemini) gemini.Configure(apiKey, model);
            else if (_inner is Providers.GroqTranslationProvider groq) groq.Configure(apiKey, model);
            else if (_inner is Providers.OpenAiTranslationProvider openai) openai.Configure(apiKey, model);
            else if (_inner is Providers.ClaudeTranslationProvider claude) claude.Configure(apiKey, model);
            else if (_inner is Providers.DeepSeekTranslationProvider deepseek) deepseek.Configure(apiKey, model);
            else if (_inner is Providers.KimiTranslationProvider kimi) kimi.Configure(apiKey, model);
            else if (_inner is Providers.QwenTranslationProvider qwen) qwen.Configure(apiKey, model);
            else if (_inner is Providers.CustomEndpointTranslationProvider custom) custom.Configure(string.Empty, apiKey, model);
        }

        public async Task<IReadOnlyList<string>> TranslateBatchAsync(
            IReadOnlyList<string> maskedTexts,
            TranslationContext context,
            CancellationToken ct = default)
        {
            if (maskedTexts == null || maskedTexts.Count == 0)
                return Array.Empty<string>();

            var targetLangKey = string.IsNullOrWhiteSpace(context.TargetLanguage)
                ? context.TargetLanguageName
                : context.TargetLanguage;

            var results = new List<string>(maskedTexts.Count);
            var toTranslateIndices = new List<int>();
            var toTranslateTexts = new List<string>();

            // Check cache for each text
            for (int i = 0; i < maskedTexts.Count; i++)
            {
                var text = maskedTexts[i];
                if (_memory.TryGet(text, targetLangKey, out var cached))
                {
                    results.Add(cached);
                }
                else
                {
                    // Placeholder; will fill after translation
                    results.Add(string.Empty);
                    toTranslateIndices.Add(i);
                    toTranslateTexts.Add(text);
                }
            }

            if (toTranslateTexts.Count > 0)
            {
                // Translate the uncached batch
                var translations = await _inner.TranslateBatchAsync(toTranslateTexts, context, ct);
                if (translations == null) throw new InvalidOperationException("Inner provider returned null translations.");

                for (int i = 0; i < toTranslateIndices.Count; i++)
                {
                    var idx = toTranslateIndices[i];
                    var translation = i < translations.Count ? translations[i] : string.Empty;
                    results[idx] = translation;
                    // Cache it
                    _memory.Add(toTranslateTexts[i], targetLangKey, translation);
                }
            }

            // Save memory asynchronously (fire-and-forget)
            _ = _memory.SaveAsync();

            return results;
        }
    }
}