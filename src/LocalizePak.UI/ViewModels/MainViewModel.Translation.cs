using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.IO.Helpers;
using LocalizePak.Translation;
using LocalizePak.Translation.Providers;
using LocalizePak.Translation.Services;

namespace LocalizePak.UI.ViewModels;

public sealed partial class MainViewModel
{
    // ─── Çeviri Motoru & Sağlayıcı Özellikleri ──────────────────────────────

    private CancellationTokenSource? _translationCts;
    private Task? _activeTranslationTask;
    private bool _isUserPauseRequested;

    [ObservableProperty]
    private bool _isTranslating;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private double _translationProgress;

    [ObservableProperty]
    private string _elapsedTime = "00:00";

    [ObservableProperty]
    private string _estimatedTimeRemaining = "--:--";

    [ObservableProperty]
    private string _translationSpeed = "0 satır/sn";

    [ObservableProperty]
    private string _userDirective = string.Empty;

    [ObservableProperty]
    private string _systemPrompt = AppSettings.DefaultMasterPrompt;

    [ObservableProperty]
    private string _selectedProviderName = "Gemini";

    [ObservableProperty]
    private bool _enableTranslationMemory;

    [ObservableProperty]
    private ObservableCollection<GlossaryEntry> _glossaryList = [];

    // ─── Sağlayıcı API Anahtarları ve Model Seçimleri ───────────────────────

    [ObservableProperty]
    private string _geminiApiKey = string.Empty;

    [ObservableProperty]
    private string _geminiModel = "gemini-3.5-flash-lite";

    [ObservableProperty]
    private string _openRouterApiKey = string.Empty;

    [ObservableProperty]
    private string _openRouterModel = "deepseek/deepseek-chat";

    [ObservableProperty]
    private string _groqApiKey = string.Empty;

    [ObservableProperty]
    private string _groqModel = "openai/gpt-oss-20b";

    [ObservableProperty]
    private string _claudeApiKey = string.Empty;

    [ObservableProperty]
    private string _claudeModel = "claude-sonnet-5-5";

    [ObservableProperty]
    private string _openAiApiKey = string.Empty;

    [ObservableProperty]
    private string _openAiModel = "gpt-6.1-sol";

    [ObservableProperty]
    private string _deepSeekApiKey = string.Empty;

    [ObservableProperty]
    private string _deepSeekModel = "deepseek-chat";

    [ObservableProperty]
    private string _kimiApiKey = string.Empty;

    [ObservableProperty]
    private string _kimiModel = "moonshot-v1-auto";

    [ObservableProperty]
    private string _qwenApiKey = string.Empty;

    [ObservableProperty]
    private string _qwenModel = "qwen-plus";

    [ObservableProperty]
    private string _togetherApiKey = string.Empty;

    [ObservableProperty]
    private string _togetherModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo";

    [ObservableProperty]
    private string _mistralApiKey = string.Empty;

    [ObservableProperty]
    private string _mistralModel = "mistral-large-latest";

    [ObservableProperty]
    private string _deepInfraApiKey = string.Empty;

    [ObservableProperty]
    private string _deepInfraModel = "meta-llama/Meta-Llama-3.1-70B-Instruct";

    [ObservableProperty]
    private string _perplexityApiKey = string.Empty;

    [ObservableProperty]
    private string _perplexityModel = "sonar-pro";

    [ObservableProperty]
    private string _customEndpointUrl = "http://localhost:11434/v1";

    [ObservableProperty]
    private string _customEndpointApiKey = string.Empty;

    [ObservableProperty]
    private string _customEndpointModel = "qwen2.5";

    // ─── Sağlayıcı ve Model Listeleri ───────────────────────────────────────

    public static IReadOnlyList<string> ProviderNames { get; } =
        [
            "Gemini",
            "OpenRouter (Hepsi Bir Arada)",
            "Together AI (Çoklu Model)",
            "Mistral AI",
            "DeepInfra (Ekonomik)",
            "Perplexity AI",
            "Groq (Ücretsiz & Süper Hızlı)",
            "Claude (Anthropic)",
            "OpenAI",
            "DeepSeek",
            "Kimi (Moonshot)",
            "Qwen (Alibaba)",
            "Özel API / Yerel LLM"
        ];

    public static IReadOnlyList<string> OpenRouterModels { get; } =
        [
            "deepseek/deepseek-chat",
            "deepseek/deepseek-r1",
            "google/gemini-3.8-flash",
            "google/gemini-3.1-pro",
            "anthropic/claude-3.7-sonnet",
            "anthropic/claude-3.5-haiku",
            "openai/gpt-4o-mini",
            "openai/gpt-4o",
            "meta-llama/llama-3.3-70b-instruct",
            "qwen/qwen-2.5-72b-instruct",
            "mistralai/mistral-large"
        ];

    public static IReadOnlyList<string> TogetherAiModels { get; } =
        [
            "meta-llama/Llama-3.3-70B-Instruct-Turbo",
            "deepseek-ai/DeepSeek-V3",
            "deepseek-ai/DeepSeek-R1",
            "Qwen/Qwen2.5-72B-Instruct-Turbo",
            "mistralai/Mistral-Small-24B-Instruct-2501"
        ];

    public static IReadOnlyList<string> MistralModels { get; } =
        [
            "mistral-large-latest",
            "mistral-small-latest",
            "codestral-latest",
            "ministral-8b-latest"
        ];

    public static IReadOnlyList<string> DeepInfraModels { get; } =
        [
            "meta-llama/Meta-Llama-3.1-70B-Instruct",
            "deepseek-ai/DeepSeek-V3",
            "Qwen/Qwen2.5-72B-Instruct"
        ];

    public static IReadOnlyList<string> PerplexityModels { get; } =
        [
            "sonar-pro",
            "sonar"
        ];

    public static IReadOnlyList<string> GeminiModels { get; } =
        [
            "gemini-3.5-flash-lite",
            "gemini-3.1-flash-lite",
            "gemini-3.8-flash",
            "gemini-4-argon",
            "gemini-3.1-pro-preview"
        ];

    public static IReadOnlyList<string> GroqModels { get; } =
        [
            "openai/gpt-oss-120b",
            "openai/gpt-oss-20b",
            "qwen/qwen3.8-27b",
            "groq/compound",
            "groq/compound-mini"
        ];

    public static IReadOnlyList<string> ClaudeModels { get; } =
        [
            "claude-sonnet-5-5",
            "claude-haiku-5-5",
            "claude-opus-5-5",
            "claude-3-7-sonnet-20250219",
            "claude-3-5-sonnet-20241022"
        ];

    public static IReadOnlyList<string> OpenAiModels { get; } =
        [
            "gpt-6-astra",
            "gpt-6.1-sol",
            "gpt-6-luna",
            "gpt-5.6-terra",
            "gpt-4o",
            "gpt-4o-mini"
        ];

    public static IReadOnlyList<string> DeepSeekModels { get; } =
        ["deepseek-chat", "deepseek-reasoner"];

    public static IReadOnlyList<string> KimiModels { get; } =
        ["moonshot-v1-auto", "moonshot-v1-128k", "moonshot-v1-32k", "moonshot-v1-8k"];

    public static IReadOnlyList<string> QwenModels { get; } =
        ["qwen-plus", "qwen-max", "qwen-turbo", "qwen-flash", "qwen-long"];

    public static IReadOnlyList<string> OllamaModels { get; } =
        [
            "qwen3",
            "qwen2.5:32b",
            "qwen2.5:14b",
            "qwen2.5:7b",
            "llama3.3:70b",
            "llama3.2:3b",
            "llama3.2:1b",
            "deepseek-r1:32b",
            "deepseek-r1:14b",
            "deepseek-r1:8b",
            "mistral-small"
        ];

    public static IReadOnlyList<string> SourceLanguages { get; } =
        ["Otomatik Algıla", "İngilizce", "Japonca", "Çince", "Korece", "Almanca", "Fransızca", "İspanyolca", "Rusça", "Türkçe"];

    public static IReadOnlyList<string> TargetLanguages { get; } =
        ["Türkçe", "English (İngilizce)", "Русский (Rusça)", "简体中文 (Çince)", "Almanca", "Fransızca", "İspanyolca", "Japonca", "Korece", "İtalyanca", "Portekizce", "Arapça"];

    public static IReadOnlyList<string> AppLanguageOptions { get; } =
        ["Türkçe", "English", "Русский", "简体中文"];

    [ObservableProperty]
    private string _selectedAppLanguage = "Türkçe";

    partial void OnSelectedAppLanguageChanged(string value)
    {
        LocalizePak.UI.Localization.LocalizationService.Instance.SetLanguage(value);
    }

    public static IReadOnlyList<string> PromptLanguageOptions { get; } =
        ["Türkçe", "English", "Русский", "简体中文"];

    [ObservableProperty]
    private string _selectedPromptLanguage = "Türkçe";

    [RelayCommand]
    public void LoadPromptTemplateForSelectedLanguage()
    {
        SystemPrompt = AppSettings.GetMasterPromptForLanguage(SelectedPromptLanguage);
    }

    public static IReadOnlyList<string> ReasoningEffortOptions { get; } =
        [
            "Kapalı (En Hızlı)",
            "Düşük (Low - Hızlı Düşünme)",
            "Orta (Medium - Dengeli)",
            "Yüksek (High - Derin Edebi Analiz)"
        ];

    [ObservableProperty]
    private string _selectedReasoningEffort = "Kapalı (En Hızlı)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguagePairDisplay))]
    private string _selectedSourceLanguage = "İngilizce";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguagePairDisplay))]
    private string _selectedTargetLanguage = "Türkçe";

    public string LanguagePairDisplay => $"{SelectedSourceLanguage} ➔ {SelectedTargetLanguage}";

    [RelayCommand]
    private void ClearTranslationMemory()
    {
        _orchestrator.TranslationMemory.Clear();
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameLocalizationSystem",
            "translation_memory.json");
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
        StatusMessage = "🗑️ Çeviri belleği (hafıza) tamamen temizlendi ve sıfırlandı.";
    }

    // ─── Canlı Buton ve Durum Mantığı (Smart Dynamic CTA) ───────────────────

    public bool ShowActiveStartButton => !IsTranslating && !IsPaused && TotalEntries > 0;
    public bool ShowInactiveStartButton => !IsTranslating && !IsPaused && TotalEntries == 0;
    public bool ShowPauseButton => IsTranslating;
    public bool ShowResumeButton => IsPaused && !IsTranslating;
    public bool IsCancelButtonVisible => IsTranslating || IsPaused;

    public bool ShowResumeButtonDynamic =>
        !IsTranslating && !IsPaused && CompletedEntries > 0 && PendingEntries > 0;

    public bool ShowSecondaryFreshStartButton =>
        !IsTranslating && !IsPaused && CompletedEntries > 0 && PendingEntries > 0;

    private void RefreshTranslationState()
    {
        OnPropertyChanged(nameof(ShowActiveStartButton));
        OnPropertyChanged(nameof(ShowInactiveStartButton));
        OnPropertyChanged(nameof(ShowPauseButton));
        OnPropertyChanged(nameof(ShowResumeButton));
        OnPropertyChanged(nameof(IsCancelButtonVisible));
        OnPropertyChanged(nameof(ShowResumeButtonDynamic));
        OnPropertyChanged(nameof(ShowSecondaryFreshStartButton));
    }

    partial void OnIsTranslatingChanged(bool value) => RefreshTranslationState();
    partial void OnIsPausedChanged(bool value) => RefreshTranslationState();

    // ─── Çeviri Çalıştırma Komutları ────────────────────────────────────────

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (LocalizationEntries.Count == 0)
        {
            await OpenPakAsync();
            return;
        }

        if (IsTranslating)
        {
            PauseTranslation();
            return;
        }

        if (IsPaused)
        {
            await ResumeTranslationAsync();
            return;
        }

        if (CompletedEntries > 0 && PendingEntries > 0)
        {
            await ResumeTranslationAsync();
            return;
        }

        await StartTranslationAsync();
    }

    [RelayCommand]
    private async Task StartTranslationAsync()
    {
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Çevrilecek girdi yok. Lütfen önce bir .pak dosyası açın.";
            return;
        }

        foreach (var vm in LocalizationEntries)
        {
            vm.Model.Status = TranslationStatus.Pending;
            vm.RefreshFromModel();
        }

        var entriesToTranslate = LocalizationEntries.Select(vm => vm.Model).ToList();
        _activeTranslationTask = ExecuteCustomTranslationAsync(entriesToTranslate, "Tüm metinler sıfırdan çevriliyor...");
        await _activeTranslationTask;
    }

    [RelayCommand]
    private void PauseTranslation()
    {
        if (!IsTranslating) return;
        _isUserPauseRequested = true;
        StatusMessage = "Çeviri duraklatılıyor, mevcut paket bekleniyor...";
        _translationCts?.Cancel();
    }

    [RelayCommand]
    private async Task ResumeTranslationAsync()
    {
        if (LocalizationEntries.Count == 0) return;

        var pendingViewModels = LocalizationEntries
            .Where(e => e.Status == TranslationStatus.Pending || e.Status == TranslationStatus.Translating)
            .ToList();

        if (pendingViewModels.Count == 0)
        {
            StatusMessage = "Kalan çevrilecek bekleyen satır bulunamadı. (Tüm satırlar tamamlandı veya hatalı).";
            return;
        }

        var entriesToTranslate = pendingViewModels.Select(vm => vm.Model).ToList();
        _activeTranslationTask = ExecuteCustomTranslationAsync(entriesToTranslate, $"Kaldığı yerden devam ediliyor ({entriesToTranslate.Count} bekleyen satır)...");
        await _activeTranslationTask;
    }

    [RelayCommand]
    private void CancelTranslation()
    {
        _isUserPauseRequested = false;
        _translationCts?.Cancel();
        IsPaused = false;
        IsTranslating = false;
        StatusMessage = "Çeviri işlemi iptal edildi.";
        RefreshTranslationState();
    }

    [RelayCommand]
    private async Task RetryErrorsAsync()
    {
        if (LocalizationEntries.Count == 0) return;

        var errorViewModels = LocalizationEntries
            .Where(e => e.Status == TranslationStatus.Error)
            .ToList();

        if (errorViewModels.Count == 0)
        {
            StatusMessage = "Yeniden denenecek hatalı satır bulunamadı.";
            return;
        }

        foreach (var vm in errorViewModels)
        {
            vm.Model.Status = TranslationStatus.Pending;
            vm.RefreshFromModel();
        }

        var entriesToTranslate = errorViewModels.Select(vm => vm.Model).ToList();
        _activeTranslationTask = ExecuteCustomTranslationAsync(entriesToTranslate, $"{entriesToTranslate.Count} hatalı satır yeniden deneniyor...");
        await _activeTranslationTask;
    }

    [RelayCommand]
    private async Task TranslateSelectedAsync()
    {
        if (LocalizationEntries.Count == 0) return;

        var selectedViewModels = LocalizationEntries
            .Where(e => e.IsSelected)
            .ToList();

        if (selectedViewModels.Count == 0)
        {
            StatusMessage = "Lütfen önce tablodaki kutucuklardan çevirmek istediğiniz satırları işaretleyin.";
            return;
        }

        foreach (var vm in selectedViewModels)
        {
            vm.Model.Status = TranslationStatus.Pending;
            vm.RefreshFromModel();
        }

        var entriesToTranslate = selectedViewModels.Select(vm => vm.Model).ToList();
        _activeTranslationTask = ExecuteCustomTranslationAsync(entriesToTranslate, $"Seçilen {entriesToTranslate.Count} satır çevriliyor...");
        await _activeTranslationTask;
    }

    private async Task ExecuteCustomTranslationAsync(IReadOnlyList<LocalizationEntry> entries, string startMessage)
    {
        var oldCts = _translationCts;
        _translationCts = null;
        if (oldCts != null)
        {
            try { oldCts.Cancel(); } catch { }
            try { oldCts.Dispose(); } catch { }
        }

        CancellationTokenSource cts;
        try { cts = new CancellationTokenSource(); }
        catch { return; }
        _translationCts = cts;

        IsTranslating = true;
        IsPaused = false;
        TranslationProgress = 0;
        ErrorCount = 0;
        CompletedEntries = 0;
        ElapsedTime = "00:00";
        EstimatedTimeRemaining = "Hesaplanıyor...";
        TranslationSpeed = "0 satır/sn";
        StatusMessage = startMessage;

        var sw = Stopwatch.StartNew();
        var provider = CreateProvider();
        var context = new TranslationContext
        {
            UserDirective = UserDirective,
            SystemPromptTemplate = SystemPrompt,
            ReasoningEffort = SelectedReasoningEffort,
            SourceLanguage = MapLanguageToIsoCode(SelectedSourceLanguage),
            SourceLanguageName = SelectedSourceLanguage,
            TargetLanguage = MapLanguageToIsoCode(SelectedTargetLanguage),
            TargetLanguageName = SelectedTargetLanguage,
            Glossary = GlossaryList.ToList()
        };

        var progressReporter = new Progress<Core.Interfaces.TranslationProgress>(p =>
        {
            CompletedEntries = p.CompletedEntries;
            ErrorCount = p.ErrorCount;
            TranslationProgress = p.Percentage;

            if (sw.Elapsed.TotalSeconds > 1)
            {
                ElapsedTime = $"{sw.Elapsed.Minutes:D2}:{sw.Elapsed.Seconds:D2}";
                var processed = p.CompletedEntries + p.ErrorCount;
                if (processed > 0)
                {
                    var speed = processed / sw.Elapsed.TotalSeconds;
                    TranslationSpeed = $"{speed:F1} satır/sn";

                    var remainingEntries = p.TotalEntries - processed;
                    if (remainingEntries > 0 && speed > 0)
                    {
                        var etaSec = remainingEntries / speed;
                        var eta = TimeSpan.FromSeconds(etaSec);
                        EstimatedTimeRemaining = $"~{eta.Minutes:D2}:{eta.Seconds:D2}";
                    }
                    else
                    {
                        EstimatedTimeRemaining = "00:00";
                    }
                }
            }

            StatusMessage = p.CurrentBatchInfo ?? $"{p.CompletedEntries + p.ErrorCount}/{p.TotalEntries} işlendi ({p.CompletedEntries} başarılı, {p.ErrorCount} hata)";

            if (p.CompletedEntries > 0 && p.CompletedEntries % 50 == 0)
            {
                _ = RecoveryService.SaveRecoverySnapshotAsync(
                    _currentLocresFile?.SourcePath ?? LoadedPakFileName,
                    entries);
            }

            var visibleCount = Math.Min(60, FilteredEntries.Count);
            for (int i = 0; i < visibleCount; i++)
            {
                FilteredEntries[i].RefreshFromModel();
            }
        });

        try
        {
            _orchestrator.EnableTranslationMemory = EnableTranslationMemory;
            bool isLocalLlm = SelectedProviderName == "Özel API / Yerel LLM";
            int batchSize = isLocalLlm ? 10 : 25;
            int concurrency = isLocalLlm ? 1 : 3;

            var token = cts.Token;

            await _orchestrator.TranslateEntriesAsync(
                entries,
                provider,
                context,
                batchSize: batchSize,
                maxConcurrency: concurrency,
                progress: progressReporter,
                ct: token);

            sw.Stop();
            RecoveryService.ClearRecoverySnapshot();
            EstimatedTimeRemaining = "Tamamlandı";
            IsPaused = false;
            StatusMessage = WithCacheSummary($"İşlem tamamlandı! Süre: {ElapsedTime}, {CompletedEntries}/{entries.Count} satır başarıyla çevrildi, {ErrorCount} hata.", provider);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Dispatcher.UIThread.Post(() =>
            {
                if (_isUserPauseRequested)
                {
                    IsPaused = true;
                    StatusMessage = "Çeviri duraklatıldı. Kaldığınız yerden devam etmek için 'Devam Et' butonuna basabilirsiniz.";
                    EstimatedTimeRemaining = "Duraklatıldı";
                }
                else
                {
                    IsPaused = false;
                    StatusMessage = WithCacheSummary("Çeviri kullanıcı tarafından iptal edildi.", provider);
                    EstimatedTimeRemaining = "İptal Edildi";
                }
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            StatusMessage = WithCacheSummary($"Çeviri hatası: {ex.Message}", provider);
        }
        finally
        {
            _isUserPauseRequested = false;
            IsTranslating = false;
            TranslationProgress = 100;

            foreach (var vm in LocalizationEntries)
            {
                vm.RefreshFromModel();
            }

            ApplyFilter();
            CompletedEntries = LocalizationEntries.Count(e => e.Status == TranslationStatus.Done || e.Status == TranslationStatus.ManuallyEdited);
            ErrorCount = LocalizationEntries.Count(e => e.Status == TranslationStatus.Error);
            RefreshStats();
        }
    }

    /// <summary>Sağlayıcı önbellek raporluyorsa (Claude / Gemini) durum mesajına önbellek özetini ekler.</summary>
    private static string WithCacheSummary(string message, ITranslationProvider provider) =>
        provider is IPromptCacheReporter reporter && reporter.CacheStats.ToSummary() is { Length: > 0 } summary
            ? $"{message} {summary}"
            : message;

    public static string MapLanguageToIsoCode(string? languageName) =>
        TranslationPromptHelper.MapLanguageToIsoCode(languageName);

    public ITranslationProvider CreateProvider()
    {
        var http = CreateHttpClient(timeoutSeconds: 120);

        ITranslationProvider baseProvider = SelectedProviderName switch
        {
            "OpenRouter (Hepsi Bir Arada)" => ConfigureOpenRouter(new OpenRouterTranslationProvider(http), OpenRouterApiKey, OpenRouterModel),
            "Together AI (Çoklu Model)" => ConfigureTogether(new TogetherAiTranslationProvider(http), TogetherApiKey, TogetherModel),
            "Mistral AI" => ConfigureMistral(new MistralTranslationProvider(http), MistralApiKey, MistralModel),
            "DeepInfra (Ekonomik)" => ConfigureDeepInfra(new DeepInfraTranslationProvider(http), DeepInfraApiKey, DeepInfraModel),
            "Perplexity AI" => ConfigurePerplexity(new PerplexityTranslationProvider(http), PerplexityApiKey, PerplexityModel),
            "Groq (Ücretsiz & Süper Hızlı)" => ConfigureGroq(new GroqTranslationProvider(http), GroqApiKey, GroqModel),
            "Claude (Anthropic)" => ConfigureClaude(new ClaudeTranslationProvider(http), ClaudeApiKey, ClaudeModel),
            "OpenAI" => ConfigureOpenAi(new OpenAiTranslationProvider(http), OpenAiApiKey, OpenAiModel),
            "DeepSeek" => ConfigureDeepSeek(new DeepSeekTranslationProvider(http), DeepSeekApiKey, DeepSeekModel),
            "Kimi (Moonshot)" => ConfigureKimi(new KimiTranslationProvider(http), KimiApiKey, KimiModel),
            "Qwen (Alibaba)" => ConfigureQwen(new QwenTranslationProvider(http), QwenApiKey, QwenModel),
            "Özel API / Yerel LLM" => ConfigureCustom(new CustomEndpointTranslationProvider(http), CustomEndpointUrl, CustomEndpointApiKey, CustomEndpointModel),
            _ => ConfigureGemini(new GeminiTranslationProvider(http), GeminiApiKey, GeminiModel)
        };

        return baseProvider;
    }

    private static ITranslationProvider ConfigureOpenRouter(OpenRouterTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureTogether(TogetherAiTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureMistral(MistralTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureDeepInfra(DeepInfraTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigurePerplexity(PerplexityTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureGroq(GroqTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureClaude(ClaudeTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureCustom(CustomEndpointTranslationProvider p, string url, string key, string model)
    { p.Configure(url, key, model); return p; }
    private static ITranslationProvider ConfigureOpenAi(OpenAiTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureDeepSeek(DeepSeekTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureKimi(KimiTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureQwen(QwenTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }
    private static ITranslationProvider ConfigureGemini(GeminiTranslationProvider p, string key, string model)
    { p.Configure(key, model); return p; }

    // ─── Tekil Satır Düzenleme & Yardımcı Komutlar ──────────────────────────

    [RelayCommand]
    public void CopySourceToTranslation(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null) return;
        item.TranslatedText = item.SourceText;
        item.Model.Status = TranslationStatus.ManuallyEdited;
        item.RefreshFromModel();
        StatusMessage = $"🔄 '{item.Key}' için kaynak metin çeviriye aktarıldı.";
    }

    /// <summary>
    /// Sıfır token harcayarak tablodaki tüm metinleri sahte yerelleştirme (Pseudolocalization) ile doldurur.
    /// Arayüz taşmalarını ve font gliflerini oyun içinde hemen test etmek için kullanılır.
    /// </summary>
    [RelayCommand]
    public void ApplyPseudolocalization()
    {
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Önce bir .pak veya lokalizasyon dosyası açmalısınız.";
            return;
        }

        int count = 0;
        foreach (var entry in LocalizationEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.SourceText))
            {
                entry.TranslatedText = PseudolocalizationEngine.Transform(entry.SourceText);
                entry.Model.Status = TranslationStatus.Done;
                entry.RefreshFromModel();
                count++;
            }
        }

        ApplyFilter();
        CompletedEntries = LocalizationEntries.Count(e => e.Status == TranslationStatus.Done || e.Status == TranslationStatus.ManuallyEdited);
        RefreshStats();
        StatusMessage = $"🧪 Sahte Yerelleştirme (Pseudolocalization) uygulandı: {count} satır dönüştürüldü. Artık test yaması üretebilirsiniz!";
    }

    [RelayCommand]
    public async Task RetranslateSingleAsync(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null || string.IsNullOrWhiteSpace(item.SourceText)) return;

        try
        {
            item.Model.Status = TranslationStatus.Translating;
            item.RefreshFromModel();
            StatusMessage = $"'{item.Key}' satırı tek başına çevriliyor...";

            var provider = CreateProvider();
            var context = new TranslationContext
            {
                UserDirective = UserDirective,
                SystemPromptTemplate = SystemPrompt,
                ReasoningEffort = SelectedReasoningEffort,
                SourceLanguage = MapLanguageToIsoCode(SelectedSourceLanguage),
                SourceLanguageName = SelectedSourceLanguage,
                TargetLanguage = MapLanguageToIsoCode(SelectedTargetLanguage),
                TargetLanguageName = SelectedTargetLanguage,
                Glossary = GlossaryList.ToList()
            };

            var masking = new MaskingEngine();
            var masked = masking.Mask(item.SourceText, context.Glossary);
            var resultList = await provider.TranslateBatchAsync([masked.MaskedText], context);

            if (resultList.Count > 0 && !string.IsNullOrWhiteSpace(resultList[0]))
            {
                var restored = masking.RestoreAndRepair(resultList[0], masked.RestoreMap);
                item.TranslatedText = restored;
                item.Model.Status = TranslationStatus.Done;
                item.RefreshFromModel();
                StatusMessage = $"✅ '{item.Key}' başarıyla yeniden çevrildi.";
            }
            else
            {
                item.Model.Status = TranslationStatus.Error;
                item.RefreshFromModel();
                StatusMessage = $"❌ '{item.Key}' çevirisi boş döndü.";
            }
        }
        catch (Exception ex)
        {
            item.Model.Status = TranslationStatus.Error;
            item.RefreshFromModel();
            StatusMessage = $"❌ Tekli çeviri hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenInGoogleTranslate(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null || string.IsNullOrEmpty(item.SourceText)) return;
        try
        {
            var sl = MapLanguageToIsoCode(SelectedSourceLanguage);
            var tl = MapLanguageToIsoCode(SelectedTargetLanguage);
            var url = $"https://translate.google.com/?sl={sl}&tl={tl}&text={Uri.EscapeDataString(item.SourceText)}&op=translate";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public void MarkAsDone(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null) return;
        item.Model.Status = TranslationStatus.Done;
        item.RefreshFromModel();
        RecountStatuses();
    }

    [RelayCommand]
    public void MarkAsPending(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null) return;
        item.Model.Status = TranslationStatus.Pending;
        item.RefreshFromModel();
        RecountStatuses();
    }

    public static async Task SetClipboardTextAsync(string text)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard is { } clipboard)
            {
                var transfer = new Avalonia.Input.DataTransfer();
                transfer.Add(Avalonia.Input.DataTransferItem.CreateText(text));
                await clipboard.SetDataAsync(transfer);
            }
        }
        catch { }
    }

    [RelayCommand]
    private void ResetSystemPromptToDefault()
    {
        SystemPrompt = AppSettings.GetMasterPromptForLanguage(SelectedPromptLanguage);
    }

    // ─── Yerel Model Kurulum Merkezi (Ollama Hub) ───────────────────────────

    public static IReadOnlyList<LocalAiModelOption> PresetLocalAiModels => OllamaLocalModelService.PresetModels;

    [ObservableProperty]
    private LocalAiModelOption _selectedLocalAiModel = OllamaLocalModelService.PresetModels[1];

    [ObservableProperty]
    private bool _isOllamaRunning;

    [ObservableProperty]
    private bool _isCheckingOllama;

    [ObservableProperty]
    private string _ollamaStatusText = "Bağlantı durumu kontrol ediliyor...";

    [ObservableProperty]
    private bool _isModelDownloading;

    [ObservableProperty]
    private double _modelDownloadPercentage;

    [ObservableProperty]
    private string _modelDownloadStatusMessage = string.Empty;

    [ObservableProperty]
    private string _modelDownloadDetail = string.Empty;

    [ObservableProperty]
    private bool _showModelInstructions;

    [ObservableProperty]
    private string _installedModelSuccessName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _installedOllamaModels = [];

    private CancellationTokenSource? _modelDownloadCts;
    private readonly OllamaLocalModelService _ollamaService = new();

    [RelayCommand]
    public async Task CheckOllamaStatusAsync()
    {
        IsCheckingOllama = true;
        try
        {
            var running = await _ollamaService.CheckOllamaRunningAsync(CustomEndpointUrl);
            IsOllamaRunning = running;
            if (running)
            {
                var installed = await _ollamaService.GetInstalledModelsAsync(CustomEndpointUrl);
                InstalledOllamaModels.Clear();
                foreach (var m in installed) InstalledOllamaModels.Add(m);
                OllamaStatusText = $"🟢 Ollama Aktif ve Bağlı ({installed.Count} model kurulu)";
            }
            else
            {
                OllamaStatusText = "🔴 Ollama sunucusu (http://localhost:11434) tespit edilemedi. Yerel modeller için Ollama açık olmalıdır.";
            }
        }
        catch (Exception ex)
        {
            IsOllamaRunning = false;
            OllamaStatusText = $"⚠️ Bağlantı hatası: {ex.Message}";
        }
        finally
        {
            IsCheckingOllama = false;
        }
    }

    [RelayCommand]
    public async Task InstallSelectedLocalModelAsync()
    {
        if (SelectedLocalAiModel == null) return;
        var modelToInstall = SelectedLocalAiModel.Id;

        var isRunning = await _ollamaService.CheckOllamaRunningAsync(CustomEndpointUrl);
        if (!isRunning)
        {
            OllamaStatusText = "🔴 Model indirilemedi: Ollama sunucusu açık değil. Lütfen Ollama'yı başlatın.";
            IsOllamaRunning = false;
            return;
        }
        IsOllamaRunning = true;

        IsModelDownloading = true;
        ModelDownloadPercentage = 0;
        ModelDownloadStatusMessage = $"{modelToInstall} indiriliyor ve kuruluyor...";
        ModelDownloadDetail = "Ollama sunucusuna bağlanılıyor...";
        ShowModelInstructions = false;

        _modelDownloadCts?.Dispose();
        _modelDownloadCts = new CancellationTokenSource();

        try
        {
            await _ollamaService.PullModelAsync(
                modelToInstall,
                progress =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ModelDownloadPercentage = progress.Percentage;
                        ModelDownloadStatusMessage = string.IsNullOrWhiteSpace(progress.Status) ? "İndiriliyor..." : progress.Status;

                        if (progress.TotalBytes > 0)
                        {
                            var compMb = progress.CompletedBytes / 1024.0 / 1024.0;
                            var totMb = progress.TotalBytes / 1024.0 / 1024.0;
                            if (totMb >= 1024)
                            {
                                ModelDownloadDetail = $"{compMb / 1024.0:F2} GB / {totMb / 1024.0:F2} GB (%{progress.Percentage:F1})";
                            }
                            else
                            {
                                ModelDownloadDetail = $"{compMb:F0} MB / {totMb:F0} MB (%{progress.Percentage:F1})";
                            }
                        }
                    });
                },
                CustomEndpointUrl,
                _modelDownloadCts.Token);

            InstalledModelSuccessName = modelToInstall;
            CustomEndpointModel = modelToInstall;
            CustomEndpointUrl = "http://localhost:11434/v1";
            SelectedProviderName = "Özel API / Yerel LLM";
            ShowModelInstructions = true;
            ModelDownloadStatusMessage = $"✅ {modelToInstall} başarıyla kuruldu ve aktif motor olarak ayarlandı!";
            ModelDownloadDetail = "Ollama üzerinden sınırsız ve kotasız çeviriye hazırsınız.";

            await CheckOllamaStatusAsync();
        }
        catch (OperationCanceledException)
        {
            ModelDownloadStatusMessage = "İndirme işlemi kullanıcı tarafından iptal edildi.";
            ModelDownloadDetail = string.Empty;
        }
        catch (Exception ex)
        {
            ModelDownloadStatusMessage = $"❌ İndirme başarısız: {ex.Message}";
            ModelDownloadDetail = "Ollama konsolunu veya disk alanınızı kontrol edin.";
        }
        finally
        {
            IsModelDownloading = false;
        }
    }

    [RelayCommand]
    public void CancelModelInstall()
    {
        _modelDownloadCts?.Cancel();
    }

    [RelayCommand]
    public void OpenOllamaWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ollama.com",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task CopySourceAsync(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null || string.IsNullOrEmpty(item.SourceText)) return;
        await SetClipboardTextAsync(item.SourceText);
        StatusMessage = "📋 Kaynak metin panoya kopyalandı.";
    }

    [RelayCommand]
    public async Task CopyTranslationAsync(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null || string.IsNullOrEmpty(item.TranslatedText)) return;
        await SetClipboardTextAsync(item.TranslatedText);
        StatusMessage = "📋 Çeviri metni panoya kopyalandı.";
    }
}
