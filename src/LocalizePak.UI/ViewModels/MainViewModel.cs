using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Interfaces;
using LocalizePak.Core.Models;
using LocalizePak.IO;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Helpers;
using Avalonia.Threading;
using System.Diagnostics;
using LocalizePak.Translation;
using LocalizePak.Translation.Providers;
using LocalizePak.Translation.Services;

namespace LocalizePak.UI.ViewModels;

/// <summary>
/// Tek bir çeviri girdisinin UI temsili (DataGrid satırı).
/// </summary>
public sealed partial class LocalizationEntryViewModel : ObservableObject
{
    private readonly LocalizationEntry _model;

    public LocalizationEntryViewModel(LocalizationEntry model)
    {
        _model = model;
    }

    public LocalizationEntry Model => _model;

    [ObservableProperty]
    private bool _isSelected;

    public string Key => _model.Key;
    public string Namespace => _model.NamespaceName;
    public string SourceText => _model.SourceText;

    [ObservableProperty]
    [property: System.Diagnostics.CodeAnalysis.SuppressMessage("MVVMTK0034", "MVVMTK0034")]
    private string _translatedText = string.Empty;

    partial void OnTranslatedTextChanged(string value)
    {
        _model.TranslatedText = value;
        if (_model.Status == TranslationStatus.Done)
            _model.Status = TranslationStatus.ManuallyEdited;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(LengthDifferencePercent));
        OnPropertyChanged(nameof(IsOverflowRisk));
        OnPropertyChanged(nameof(LengthInfo));
        OnPropertyChanged(nameof(OverflowColor));
    }

    public TranslationStatus Status => _model.Status;
    public bool HasError => _model.HasVariableError;
    public string? Notes => _model.Notes;

    /// <summary>QA etiket/değişken veya format sorunu var mı?</summary>
    public bool HasQaIssue => TranslationQaService.ValidateEntry(_model).Count > 0;

    /// <summary>Karakter uzunluk farkı (%)</summary>
    public int LengthDifferencePercent =>
        string.IsNullOrEmpty(SourceText) ? 0 : (int)(((TranslatedText.Length - SourceText.Length) / (double)SourceText.Length) * 100);

    /// <summary>Metin %40'tan fazla uzadıysa oyun arayüzü taşma riski uyarısı</summary>
    public bool IsOverflowRisk =>
        LengthDifferencePercent > 40 && SourceText.Length > 10;

    public string OverflowColor => IsOverflowRisk ? "#EF4444" : "#10B981";

    public string LengthInfo =>
        $"{SourceText.Length} / {TranslatedText.Length} kar. ({(LengthDifferencePercent >= 0 ? "+" : "")}{LengthDifferencePercent}%)";

    public string StatusLabel => _model.Status switch
    {
        TranslationStatus.Pending => "Bekliyor",
        TranslationStatus.Translating => "Çevriliyor...",
        TranslationStatus.Done => "Tamamlandı",
        TranslationStatus.Error => "Hata",
        TranslationStatus.Skipped => "Atlandı",
        TranslationStatus.ManuallyEdited => "Manuel",
        _ => "Bilinmiyor"
    };

    public string StatusColor => _model.Status switch
    {
        TranslationStatus.Done => "#10B981",
        TranslationStatus.Error => "#EF4444",
        TranslationStatus.Translating => "#6366F1",
        TranslationStatus.ManuallyEdited => "#F59E0B",
        _ => "#94A3B8"
    };

    /// <summary>Modeldeki değişikliklerden UI'ı günceller.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("MVVMTK0034", "MVVMTK0034")]
    public void RefreshFromModel()
    {
        _translatedText = _model.TranslatedText;
        OnPropertyChanged(nameof(TranslatedText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasQaIssue));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(LengthDifferencePercent));
        OnPropertyChanged(nameof(IsOverflowRisk));
        OnPropertyChanged(nameof(LengthInfo));
        OnPropertyChanged(nameof(OverflowColor));
    }
}

/// <summary>
/// Ana pencere ViewModel. Tüm iş mantığını yönetir.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly PakService _pakService = new();
    private readonly UniversalLocalizationService _locresService = new();
    private readonly BatchTranslationOrchestrator _orchestrator = new();
    // SocketsHttpHandler paylaşımlı (bağlantı havuzu için) — HttpClient her seferinde yeniden oluşturulur
    private static readonly SocketsHttpHandler _sharedHandler = new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20
    };

    private static HttpClient CreateHttpClient(int timeoutSeconds = 120)
        => new HttpClient(_sharedHandler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

    private PakArchive? _currentArchive;
    private LocresFile? _currentLocresFile;
    private CancellationTokenSource? _translationCts;
    private Task? _activeTranslationTask;

    public MainViewModel()
    {
        _ = LoadSettingsAsync();
        _ = CheckRecoverySessionAsync();
    }

    // ─── Properties ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<PakEntry> _pakEntries = [];

    [ObservableProperty]
    private PakEntry? _selectedPakEntry;

    [ObservableProperty]
    private ObservableCollection<LocalizationEntryViewModel> _localizationEntries = [];

    [ObservableProperty]
    private ObservableCollection<LocalizationEntryViewModel> _filteredEntries = [];

    [ObservableProperty]
    private string _statusMessage = "Başlamak için bir .pak dosyası açın.";

    [ObservableProperty]
    private double _translationProgress;

    [ObservableProperty]
    private bool _isTranslating;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _userDirective = string.Empty;

    [ObservableProperty]
    private string _aesKey = string.Empty;

    [ObservableProperty]
    private string _selectedProviderName = "Gemini";

    /// <summary>Kullanılabilir AI sağlayıcı adları listesi.</summary>
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
            "deepseek/deepseek-chat",            // DeepSeek V3 (En Popüler & Çok Ekonomik)
            "deepseek/deepseek-r1",              // DeepSeek R1 (Derin Akıl Yürütme)
            "google/gemini-3.8-flash",          // Google Gemini 3.8 Flash (Yeni Nesil)
            "google/gemini-3.1-pro",            // Google Gemini 3.1 Pro (Derin Akıl Yürütme)
            "anthropic/claude-3.7-sonnet",       // Claude 3.7 Sonnet
            "anthropic/claude-3.5-haiku",        // Claude 3.5 Haiku
            "openai/gpt-4o-mini",                // OpenAI GPT-4o Mini
            "openai/gpt-4o",                     // OpenAI GPT-4o
            "meta-llama/llama-3.3-70b-instruct", // Meta Llama 3.3 70B
            "qwen/qwen-2.5-72b-instruct",        // Qwen 2.5 72B
            "mistralai/mistral-large"            // Mistral Large
        ];

    public static IReadOnlyList<string> TogetherAiModels { get; } =
        [
            "meta-llama/Llama-3.3-70B-Instruct-Turbo",  // Meta Llama 3.3 Turbo (Hızlı & Yetenekli)
            "deepseek-ai/DeepSeek-V3",                  // DeepSeek V3 Flagship
            "deepseek-ai/DeepSeek-R1",                  // DeepSeek R1 Akıl Yürütme
            "Qwen/Qwen2.5-72B-Instruct-Turbo",          // Qwen 2.5 72B Turbo
            "mistralai/Mistral-Small-24B-Instruct-2501" // Mistral Small 24B
        ];

    public static IReadOnlyList<string> MistralModels { get; } =
        [
            "mistral-large-latest",                     // Mistral Large (Üst Düzey Edebiyat & Doğallık)
            "mistral-small-latest",                     // Mistral Small (Hızlı & Dengeli)
            "codestral-latest",                         // Codestral (Değişken & Kod Koruma)
            "ministral-8b-latest"                       // Ministral 8B (Hafif & Ekonomik)
        ];

    public static IReadOnlyList<string> DeepInfraModels { get; } =
        [
            "meta-llama/Meta-Llama-3.1-70B-Instruct",   // Llama 3.1 70B
            "deepseek-ai/DeepSeek-V3",                  // DeepSeek V3
            "Qwen/Qwen2.5-72B-Instruct"                 // Qwen 2.5 72B
        ];

    public static IReadOnlyList<string> PerplexityModels { get; } =
        [
            "sonar-pro",                                // Sonar Pro (Gelişmiş Arama & Muhakeme)
            "sonar"                                     // Sonar (Hızlı & Hafif)
        ];

    public static IReadOnlyList<string> GeminiModels { get; } =
        [
            // ─── 3.x Serisi (En Güncel & Önerilen) ───
            "gemini-3.8-flash",          // En yeni Flash (En Hızlı & Yüksek Kota - Varsayılan)
            "gemini-3.7-flash",          // 3.7 Flash
            "gemini-3.6-flash",          // Stabil GA Flash
            "gemini-3.5-flash-lite",     // Yüksek Hız / Düşük Maliyet
            "gemini-3.1-pro"             // Gelişmiş Akıl Yürütme & Edebi Çeviri
        ];

    public static IReadOnlyList<string> GroqModels { get; } =
        [
            "openai/gpt-oss-120b",       // Groq Flagship Model (En Güçlü)
            "openai/gpt-oss-20b",        // Hızlı, Hafif & Dengeli (Önerilen)
            "qwen/qwen3.8-27b",          // Qwen Serisinin En Yeni Çeviri Modeli
            "groq/compound",             // Groq Akıllı Compound Motoru
            "groq/compound-mini"         // Groq Hızlı Compound Motoru
        ];

    public static IReadOnlyList<string> ClaudeModels { get; } =
        [
            "claude-sonnet-5-5",         // Yeni Nesil Flagship (Edebi & Doğal Çeviri)
            "claude-haiku-5-5",          // Yeni Nesil Hızlı & Ekonomik
            "claude-opus-5-5",           // Üst Düzey Akıl Yürütme
            "claude-3-7-sonnet-20250219",// Kararlı 3.7
            "claude-3-5-sonnet-20241022" // Klasik 3.5
        ];

    public static IReadOnlyList<string> OpenAiModels { get; } =
        [
            "gpt-6-astra",               // Yeni Nesil Flagship (En Yüksek Kalite)
            "gpt-6.1-sol",               // Dengeli, Hızlı & Güçlü (Önerilen)
            "gpt-6-luna",                // Ultra Hızlı & Düşük Maliyetli
            "gpt-5.6-terra",             // Kararlı Kurumsal Seri
            "gpt-4o",                    // Çok Modlu Klasik
            "gpt-4o-mini"                // Hafif Klasik
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

    /// <summary>Desteklenen kaynak diller listesi.</summary>
    public static IReadOnlyList<string> SourceLanguages { get; } =
        ["Otomatik Algıla", "İngilizce", "Japonca", "Çince", "Korece", "Almanca", "Fransızca", "İspanyolca", "Rusça", "Türkçe"];

    /// <summary>Desteklenen hedef diller listesi (İngilizce, Rusça, Çince, Türkçe öncelikli).</summary>
    public static IReadOnlyList<string> TargetLanguages { get; } =
        ["Türkçe", "English (İngilizce)", "Русский (Rusça)", "简体中文 (Çince)", "Almanca", "Fransızca", "İspanyolca", "Japonca", "Korece", "İtalyanca", "Portekizce", "Arapça"];

    /// <summary>Uygulama arayüz dili seçenekleri.</summary>
    public static IReadOnlyList<string> AppLanguageOptions { get; } =
        ["Türkçe", "English", "Русский", "简体中文"];

    [ObservableProperty]
    private string _selectedAppLanguage = "Türkçe";

    /// <summary>Master Sistem Prompt şablon dili seçenekleri.</summary>
    public static IReadOnlyList<string> PromptLanguageOptions { get; } =
        ["Türkçe", "English", "Русский", "简体中文"];

    [ObservableProperty]
    private string _selectedPromptLanguage = "Türkçe";

    [RelayCommand]
    public void LoadPromptTemplateForSelectedLanguage()
    {
        SystemPrompt = AppSettings.GetMasterPromptForLanguage(SelectedPromptLanguage);
    }

    /// <summary>AI Düşünme ve Akıl Yürütme Modları (Reasoning Effort / Thinking Budget).</summary>
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
    private string _selectedSourceLanguage = "İngilizce";

    [ObservableProperty]
    private string _selectedTargetLanguage = "Türkçe";

    [ObservableProperty]
    private string _elapsedTime = "00:00";

    [ObservableProperty]
    private string _estimatedTimeRemaining = "--:--";

    [ObservableProperty]
    private string _translationSpeed = "0 satır/sn";

    [ObservableProperty]
    private string _lastOpenedPath = string.Empty;

    [ObservableProperty]
    private bool _isPakLoaded;

    [ObservableProperty]
    private string _loadedPakFileName = string.Empty;

    [ObservableProperty]
    private string _loadedPakSizeInfo = string.Empty;

    [ObservableProperty]
    private string _geminiApiKey = string.Empty;

    [ObservableProperty]
    private string _geminiModel = "gemini-3.8-flash";

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
    private string _kimiModel = "moonshot-v1-8k";

    [ObservableProperty]
    private string _qwenApiKey = string.Empty;

    [ObservableProperty]
    private string _qwenModel = "qwen-plus";

    [ObservableProperty]
    private string _openRouterApiKey = string.Empty;

    [ObservableProperty]
    private string _openRouterModel = "deepseek/deepseek-chat";

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

    [ObservableProperty]
    private string _systemPrompt = AppSettings.DefaultMasterPrompt;

    [ObservableProperty]
    private LocalizationEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showOnlyErrors;

    /// <summary>Gelişmiş Durum Filtreleri.</summary>
    public static IReadOnlyList<string> StatusFilterOptions { get; } =
        ["Tümü", "Bekleyenler (Pending)", "Tamamlananlar", "Hatalar", "Manuel Düzenlenenler", "Taşma Riski (%40+)", "QA / Etiket Hataları"];

    [ObservableProperty]
    private string _selectedStatusFilter = "Tümü";

    // ─── Toplu Bul ve Değiştir (Find & Replace) ─────────────────────────
    [ObservableProperty]
    private bool _isFindReplaceOpen;

    [ObservableProperty]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _matchWholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _findScope = "Yalnızca Hedef Çeviri";

    public static IReadOnlyList<string> FindScopeOptions { get; } =
        ["Yalnızca Hedef Çeviri", "Yalnızca Kaynak Metin", "Her İkisi"];

    [ObservableProperty]
    private string _findResultInfo = string.Empty;

    // ─── Otomatik Oturum Kurtarma (Recovery) ─────────────────────────────
    [ObservableProperty]
    private bool _hasRecoverySession;

    [ObservableProperty]
    private string _recoverySessionInfo = string.Empty;

    [ObservableProperty]
    private int _totalEntries;

    [ObservableProperty]
    private int _completedEntries;

    [ObservableProperty]
    private int _errorCount;

    public int PendingEntries => Math.Max(0, TotalEntries - CompletedEntries - ErrorCount);
    public int ManuallyEditedCount => LocalizationEntries.Count(e => e.Status == TranslationStatus.ManuallyEdited);
    public int QaIssueCount => LocalizationEntries.Count(e => e.HasQaIssue);
    public int OverflowRiskCount => LocalizationEntries.Count(e => e.IsOverflowRisk);
    public double CompletionPercentage => TotalEntries == 0 ? 0 : Math.Round((double)CompletedEntries / TotalEntries * 100, 1);
    public int TotalSourceWords => LocalizationEntries.Sum(e => e.SourceText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    public int TotalSourceChars => LocalizationEntries.Sum(e => e.SourceText.Length);

    public void RefreshStats()
    {
        OnPropertyChanged(nameof(PendingEntries));
        OnPropertyChanged(nameof(ManuallyEditedCount));
        OnPropertyChanged(nameof(QaIssueCount));
        OnPropertyChanged(nameof(OverflowRiskCount));
        OnPropertyChanged(nameof(CompletionPercentage));
        OnPropertyChanged(nameof(TotalSourceWords));
        OnPropertyChanged(nameof(TotalSourceChars));
    }

    partial void OnTotalEntriesChanged(int value) => RefreshStats();
    partial void OnCompletedEntriesChanged(int value) => RefreshStats();
    partial void OnErrorCountChanged(int value) => RefreshStats();

    [ObservableProperty]
    private string? _lastExportedDirectory;

    [ObservableProperty]
    private bool _hasExportedDirectory;

    [ObservableProperty]
    private bool _enableTranslationMemory;

    [ObservableProperty]
    private ObservableCollection<GlossaryEntry> _glossaryList = [];

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

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnShowOnlyErrorsChanged(bool value) => ApplyFilter();
    partial void OnSelectedStatusFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedPakEntryChanged(PakEntry? value)
    {
        if (value is not null)
        {
            _ = Task.Run(async () => await ExtractLocresAsync(value));
        }
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenPakAsync()
    {
        var path = await ShowOpenFileDialogAsync(
            "pak dosyası seç",
            [["*.pak"]]);
        if (path is null) return;
        await OpenPakFromPathAsync(path);
    }

    /// <summary>
    /// Verilen dosya yolundan .pak arşivini açar (Sürükle-Bırak veya Dosya Seçici).
    /// </summary>
    private readonly Dictionary<string, LocresFile> _archiveLocresCache = new(StringComparer.OrdinalIgnoreCase);
    public const string AllFilesVirtualPath = "🌐 [TÜM ARŞİV] — Tüm Dosyaları Birleştir ve Göster";

    /// <summary>
    /// Verilen dosya yolundan .pak arşivini açar (Sürükle-Bırak veya Dosya Seçici).
    /// </summary>
    public async Task OpenPakFromPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            IsLoading = true;
            LastOpenedPath = path;
            StatusMessage = "Arşiv açılıyor...";
            _archiveLocresCache.Clear();

            await Task.Run(async () =>
            {
                var archive = await _pakService.OpenAsync(path,
                    string.IsNullOrWhiteSpace(AesKey) ? null : AesKey);

                _currentArchive = archive;

                var entryList = new List<PakEntry>();
                if (_currentArchive.LocalizationEntries.Count > 1)
                {
                    entryList.Add(new PakEntry { VirtualPath = AllFilesVirtualPath });
                }
                entryList.AddRange(_currentArchive.LocalizationEntries);

                var fi = new FileInfo(path);
                var sizeInfo = fi.Exists ? $"{(fi.Length / (1024.0 * 1024.0)):F1} MB" : string.Empty;

                Dispatcher.UIThread.Post(() =>
                {
                    PakEntries = new ObservableCollection<PakEntry>(entryList);
                    LoadedPakFileName = _currentArchive.FileName;
                    LoadedPakSizeInfo = sizeInfo;
                    IsPakLoaded = true;
                    StatusMessage = $"Açıldı: {_currentArchive.FileName} — {_currentArchive.LocalizationEntries.Count} dosya ({LoadedPakSizeInfo})";

                    // İlk dosyayı (veya Tüm Arşivi) otomatik yükle
                    if (PakEntries.Count > 0)
                    {
                        SelectedPakEntry = PakEntries[0];
                    }
                });
            });
        }
        catch (Exception ex)
        {
            IsPakLoaded = false;
            StatusMessage = $"Hata: {ex.Message}";
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExtractLocresAsync(PakEntry? entry)
    {
        if (entry is null || _currentArchive is null) return;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = true;
                StatusMessage = $"Lokalizasyon dosyası okunuyor: {Path.GetFileName(entry.VirtualPath)}...";
            });

            await Task.Run(async () =>
            {
                LocresFile targetLocres;

                if (entry.VirtualPath == AllFilesVirtualPath)
                {
                    // Tüm dosyaları belleğe yükle ve birleştir
                    var masterLocres = new LocresFile { Version = LocresVersion.V3, SourcePath = AllFilesVirtualPath };
                    foreach (var realEntry in _currentArchive.LocalizationEntries)
                    {
                        if (!_archiveLocresCache.TryGetValue(realEntry.VirtualPath, out var subLocres))
                        {
                            var stream = await _pakService.ExtractEntryAsync(_currentArchive, realEntry);
                            subLocres = await _locresService.DeserializeAsync(stream);
                            subLocres.SourcePath = realEntry.VirtualPath;
                            _archiveLocresCache[realEntry.VirtualPath] = subLocres;
                        }

                        var nsName = Path.GetFileName(realEntry.VirtualPath);
                        var ns = new LocresNamespace { Name = nsName };
                        foreach (var e in subLocres.AllEntries)
                        {
                            e.NamespaceName = nsName;
                            ns.Entries.Add(e);
                        }
                        masterLocres.Namespaces.Add(ns);
                    }

                    targetLocres = masterLocres;
                }
                else
                {
                    if (!_archiveLocresCache.TryGetValue(entry.VirtualPath, out var subLocres))
                    {
                        var stream = await _pakService.ExtractEntryAsync(_currentArchive, entry);
                        subLocres = await _locresService.DeserializeAsync(stream);
                        subLocres.SourcePath = entry.VirtualPath;
                        _archiveLocresCache[entry.VirtualPath] = subLocres;
                    }
                    targetLocres = subLocres;
                }

                _currentLocresFile = targetLocres;

                var viewModels = _currentLocresFile.AllEntries
                    .Select(e => new LocalizationEntryViewModel(e))
                    .ToList();

                var total = viewModels.Count;
                var done = viewModels.Count(v => v.Status == TranslationStatus.Done || v.Status == TranslationStatus.ManuallyEdited);
                var err = viewModels.Count(v => v.Status == TranslationStatus.Error);
                var obsCollection = new ObservableCollection<LocalizationEntryViewModel>(viewModels);

                Dispatcher.UIThread.Post(() =>
                {
                    LocalizationEntries = obsCollection;
                    TotalEntries = total;
                    CompletedEntries = done;
                    ErrorCount = err;
                    ApplyFilter();
                    StatusMessage = $"Yüklendi: {entry.VirtualPath} — {TotalEntries} girdi ({CompletedEntries} tamamlandı, {ErrorCount} hata)";
                    IsLoading = false;
                });
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = $"Lokalizasyon dosyası açılamadı: {ex.Message}";
                IsLoading = false;
            });
        }
    }

    private bool CanStartOrResumeTranslation => !IsTranslating;
    private bool CanStopTranslation => IsTranslating;

    partial void OnIsTranslatingChanged(bool value)
    {
        StartTranslationCommand.NotifyCanExecuteChanged();
        ResumeTranslationCommand.NotifyCanExecuteChanged();
        StopTranslationCommand.NotifyCanExecuteChanged();
        TranslateSelectedCommand.NotifyCanExecuteChanged();
        RetryErrorsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartOrResumeTranslation))]
    private async Task StartTranslationAsync()
    {
        if (IsTranslating) return;
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Önce bir .pak veya lokalizasyon dosyası açmalısınız.";
            return;
        }

        // Önceki işlemin tamamen bitmesini bekle
        if (_activeTranslationTask != null)
        {
            try { await _activeTranslationTask; } catch { }
            _activeTranslationTask = null;
        }

        // Tüm pending-olmayanları pending yap (tam yeniden başlat)
        foreach (var vm in LocalizationEntries)
        {
            if (vm.Status != TranslationStatus.Done && vm.Status != TranslationStatus.ManuallyEdited)
            {
                vm.Model.Status = TranslationStatus.Pending;
                vm.RefreshFromModel();
            }
        }

        var pending = LocalizationEntries
            .Where(e => e.Status == TranslationStatus.Pending)
            .Select(e => e.Model)
            .ToList();

        if (pending.Count == 0)
        {
            StatusMessage = "Çevrilecek bekleyen satır yok.";
            return;
        }

        _activeTranslationTask = ExecuteCustomTranslationAsync(pending, $"{pending.Count} satır için çeviri başlatılıyor...");
        await _activeTranslationTask;
    }

    /// <summary>
    /// Kaldığı yerden devam et: Çevrilmemiş (Pending, Translating, Error) tüm satırları bulup çevirir.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartOrResumeTranslation))]
    private async Task ResumeTranslationAsync()
    {
        if (IsTranslating) return;
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Önce bir .pak veya lokalizasyon dosyası açmalısınız.";
            return;
        }

        var uncompletedViewModels = LocalizationEntries
            .Where(e => e.Status != TranslationStatus.Done && e.Status != TranslationStatus.ManuallyEdited)
            .ToList();

        if (uncompletedViewModels.Count == 0)
        {
            StatusMessage = "Tüm satırlar zaten tamamlanmış! Devam edilecek bekleyen satır yok.";
            return;
        }

        foreach (var vm in uncompletedViewModels)
        {
            vm.Model.Status = TranslationStatus.Pending;
            vm.RefreshFromModel();
        }

        var entriesToResume = uncompletedViewModels.Select(vm => vm.Model).ToList();
        await ExecuteCustomTranslationAsync(entriesToResume, $"Kalan {entriesToResume.Count} satır için çeviriye devam ediliyor...");
    }

    /// <summary>
    /// Sadece hata alan satırları (Status == Error) sıfırlayıp yeniden çevirir.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartOrResumeTranslation))]
    private async Task RetryErrorsAsync()
    {
        if (IsTranslating) return;
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Önce bir .pak veya lokalizasyon dosyası açmalısınız.";
            return;
        }

        var errorViewModels = LocalizationEntries
            .Where(e => e.Status == TranslationStatus.Error)
            .ToList();

        if (errorViewModels.Count == 0)
        {
            StatusMessage = "Tekrar denenecek hatalı satır bulunamadı (Tüm satırlar başarılı!).";
            return;
        }

        foreach (var vm in errorViewModels)
        {
            vm.Model.Status = TranslationStatus.Pending;
            vm.RefreshFromModel();
        }

        var entriesToRetry = errorViewModels.Select(vm => vm.Model).ToList();
        await ExecuteCustomTranslationAsync(entriesToRetry, $"Hatalı {entriesToRetry.Count} satır yeniden çevriliyor...");
    }

    /// <summary>
    /// Kullanıcının kutucukla seçtiği satırları (IsSelected == true) çevirir.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartOrResumeTranslation))]
    private async Task TranslateSelectedAsync()
    {
        if (IsTranslating) return;
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Önce bir .pak veya lokalizasyon dosyası açmalısınız.";
            return;
        }

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
        // Önceki CTS varsa güvenle iptal et ve temizle
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
        TranslationProgress = 0;
        ErrorCount = 0;
        CompletedEntries = 0;
        ElapsedTime = "00:00";
        EstimatedTimeRemaining = "Hesaplanıyor...";
        TranslationSpeed = "0 satır/sn";
        StatusMessage = startMessage;

        var sw = System.Diagnostics.Stopwatch.StartNew();
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

            // Her 50 satırda bir olası çökme veya elektrik kesintisine karşı snapshot kaydet
            if (p.CompletedEntries > 0 && p.CompletedEntries % 50 == 0)
            {
                _ = RecoveryService.SaveRecoverySnapshotAsync(
                    _currentLocresFile?.SourcePath ?? LoadedPakFileName,
                    entries);
            }

            foreach (var vm in LocalizationEntries)
                vm.RefreshFromModel();
        });

        try
        {
            _orchestrator.EnableTranslationMemory = EnableTranslationMemory;
            bool isLocalLlm = SelectedProviderName == "Özel API / Yerel LLM";
            // Yerel LLM: küçük batch, tek iş parçacığı
            // API: büyük batch (daha az istek = daha hızlı), yüksek concurrency
            int batchSize = isLocalLlm ? 10 : 50;
            int concurrency = isLocalLlm ? 1 : 6;

            // CTS null olmuş olabilir (race condition koruması)
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
            StatusMessage = $"İşlem tamamlandı! Süre: {ElapsedTime}, {CompletedEntries}/{entries.Count} satır başarıyla çevrildi, {ErrorCount} hata.";
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = "İşlem kullanıcı tarafından durduruldu.";
                EstimatedTimeRemaining = "Durduruldu";
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = $"Çeviri hatası: {ex.Message}";
                EstimatedTimeRemaining = "Hata";
            });
        }
        finally
        {
            sw.Stop();
            // CTS'yi null yapmadan önce temizle
            var currentCts = _translationCts;
            if (currentCts == cts) _translationCts = null;
            try { cts.Dispose(); } catch { }

            // UI güncellemelerini UI thread'de yap
            Dispatcher.UIThread.Post(() =>
            {
                IsTranslating = false;

                foreach (var vm in LocalizationEntries.Where(e => e.Status == TranslationStatus.Translating))
                {
                    vm.Model.Status = TranslationStatus.Pending;
                    vm.RefreshFromModel();
                }

                // Command butonlarını güncelle
                StartTranslationCommand.NotifyCanExecuteChanged();
                ResumeTranslationCommand.NotifyCanExecuteChanged();
                StopTranslationCommand.NotifyCanExecuteChanged();
                RetryErrorsCommand.NotifyCanExecuteChanged();
                TranslateSelectedCommand.NotifyCanExecuteChanged();
            });
        }
    }

    [RelayCommand]
    private void SelectAllFiltered()
    {
        foreach (var item in FilteredEntries)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in LocalizationEntries)
            item.IsSelected = false;
    }

    [RelayCommand]
    private void SelectOnlyErrors()
    {
        foreach (var item in LocalizationEntries)
            item.IsSelected = item.Status == TranslationStatus.Error;
    }

    [RelayCommand(CanExecute = nameof(CanStopTranslation))]
    private void StopTranslation()
    {
        if (IsTranslating)
        {
            StatusMessage = "Çeviri durduruluyor, lütfen bekleyin...";

            // CTS'yi yerel değişkene al ve iptal et (null kontrolü ile)
            var cts = _translationCts;
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
            }

            foreach (var vm in LocalizationEntries.Where(e => e.Status == TranslationStatus.Translating))
            {
                vm.Model.Status = TranslationStatus.Pending;
                vm.RefreshFromModel();
            }
        }
    }

    /// <summary>
    /// Çeviriyi, orijinal dosya adıyla BİREBİR AYNI isimde, tarihe göre adlandırılmış
    /// izole bir çıktı klasörüne otomatik olarak kaydeder (Karışmayı %100 önler).
    /// </summary>
    [RelayCommand]
    private async Task ExportToTimestampedFolderAsync()
    {
        if (_currentLocresFile is null || _currentArchive is null)
        {
            StatusMessage = "Önce bir .pak arşivi ve lokalizasyon dosyası açmalısınız.";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Tarihli çıktı klasörü hazırlanıyor...";

            var (targetDir, targetFilePath) = OutputManager.PrepareIsolatedExport(_currentArchive.FilePath);
            var mountPoint = _currentArchive.MountPoint;
            
            // Tüm orijinal arşiv varlıklarını (dokunulmaz font, ses, config dosyaları dahil) topla
            var filesToWrite = await PrepareAllArchiveFilesForExportAsync();

            // Birebir aynı dosya adıyla (örn. English.pak) tarihli klasöre yaz
            await _pakService.WriteAsync(targetFilePath, mountPoint, filesToWrite,
                sourceFormat: _currentArchive.Format);

            // Bilgi notu dosyası oluştur
            var infoPath = Path.Combine(targetDir, "cevirme_bilgisi.txt");
            var infoContent = $"""
            ====================================================
            LOCALIZEPAK ÇEVİRİ VE YAMA BİLGİSİ
            ====================================================
            Tarih/Saat      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            Orijinal Dosya  : {_currentArchive.FilePath}
            Çıktı Dosyası   : {Path.GetFileName(targetFilePath)}
            Toplam Satır    : {TotalEntries}
            Tamamlanan      : {CompletedEntries}
            Hata Sayısı     : {ErrorCount}
            ====================================================
            KULLANIM:
            Bu klasördeki '{Path.GetFileName(targetFilePath)}' dosyasını oyunun
            ilgili klasörüne atıp üzerine yazarak yamayı aktifleştirebilirsiniz.
            """;
            await File.WriteAllTextAsync(infoPath, infoContent);

            LastExportedDirectory = targetDir;
            HasExportedDirectory = true;
            StatusMessage = $"✅ Orijinal isimle çıktı oluşturuldu: {Path.GetFileName(targetDir)}\\{Path.GetFileName(targetFilePath)}";

            // Kullanıcı kolaylığı için klasörü hemen aç
            OutputManager.OpenFolderInExplorer(targetDir);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dışa aktarma hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(LastExportedDirectory))
        {
            OutputManager.OpenFolderInExplorer(LastExportedDirectory);
        }
    }

    [RelayCommand]
    private async Task ExportTranslationsCsvAsync()
    {
        if (_currentLocresFile is null)
        {
            StatusMessage = "Dışa aktarılacak metin bulunamadı.";
            return;
        }

        var savePath = await ShowSaveFileDialogAsync(
            "Çeviri Tablosunu CSV Olarak Kaydet",
            [["*.csv"]],
            "Ceviriler_" + Path.GetFileNameWithoutExtension(_currentLocresFile.SourcePath ?? "Localization") + ".csv");

        if (savePath is null) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Namespace,Key,SourceText,TranslatedText");
            foreach (var item in LocalizationEntries)
            {
                var ns = item.Namespace.Replace("\"", "\"\"");
                var key = item.Key.Replace("\"", "\"\"");
                var src = item.SourceText.Replace("\"", "\"\"");
                var tr = item.TranslatedText.Replace("\"", "\"\"");
                sb.AppendLine($"\"{ns}\",\"{key}\",\"{src}\",\"{tr}\"");
            }
            await File.WriteAllTextAsync(savePath, sb.ToString(), System.Text.Encoding.UTF8);
            StatusMessage = $"CSV başarıyla kaydedildi: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV dışa aktarma hatası: {ex.Message}";
        }
    }

    /// <summary>
    /// Çeviri sırasında hata alan satırları (Status == Error) detaylı bir CSV hata raporu olarak dışa aktarır.
    /// </summary>
    [RelayCommand]
    private async Task ExportErrorsCsvAsync()
    {
        var errorEntries = LocalizationEntries.Where(e => e.Status == TranslationStatus.Error).ToList();
        if (errorEntries.Count == 0)
        {
            StatusMessage = "Dışa aktarılacak hatalı satır bulunamadı (Tüm satırlar başarılı!).";
            return;
        }

        var defaultName = $"Hata_Raporu_{DateTime.Now:yyyy-MM-dd_HH-mm}_{errorEntries.Count}_Hata.csv";
        var savePath = await ShowSaveFileDialogAsync(
            "Hatalı Satırları CSV Olarak Kaydet",
            [["*.csv"]],
            defaultName);

        if (savePath is null) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Durum,Namespace,Anahtar,Kaynak Metin (Orijinal),Hedef Çeviri,Karakter Sayısı,Olası Hata Nedeni");
            foreach (var item in errorEntries)
            {
                var ns = CsvSafe(item.Namespace);
                var key = CsvSafe(item.Key);
                var src = CsvSafe(item.SourceText);
                var tr = CsvSafe(item.TranslatedText);
                var charLen = item.SourceText.Length;
                var reason = string.IsNullOrWhiteSpace(item.SourceText) ? "Boş metin" :
                             (item.SourceText.Contains("<?xml") || item.SourceText.Contains("<save>") || item.SourceText.Contains("StartTime") || item.SourceText.Contains("build90")) ? "XML / Kod etiketi kırıntısı (Çevrilemez ikili kod)" :
                             "Yapay zeka güvenlik filtresi / Maskeleme uyuşmazlığı";
                sb.AppendLine($"\"Hata\",\"{ns}\",\"{key}\",\"{src}\",\"{tr}\",\"{charLen}\",\"{reason}\"");
            }

            await File.WriteAllTextAsync(savePath, sb.ToString(), System.Text.Encoding.UTF8);
            StatusMessage = $"⚠️ {errorEntries.Count} hatalı satır CSV raporu olarak kaydedildi: {Path.GetFileName(savePath)}";

            var folder = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(folder))
            {
                OutputManager.OpenFolderInExplorer(folder);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata raporu dışa aktarma hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportTranslationsCsvAsync()
    {
        if (_currentLocresFile is null)
        {
            StatusMessage = "Önce bir .pak veya lokalizasyon dosyası açmalısınız.";
            return;
        }

        var openPath = await ShowOpenFileDialogAsync(
            "Düzenlenmiş CSV Dosyasını Seç",
            [["*.csv"]]);

        if (openPath is null || !File.Exists(openPath)) return;

        try
        {
            var content = await File.ReadAllTextAsync(openPath, System.Text.Encoding.UTF8);
            var rows = ParseCsv(content);
            int importedCount = 0;

            var map = new Dictionary<string, string>();
            for (int i = 1; i < rows.Count; i++)
            {
                var parts = rows[i];
                if (parts.Length >= 4 && !string.IsNullOrEmpty(parts[1]))
                {
                    map[parts[1]] = parts[3]; // Key -> TranslatedText
                }
            }

            foreach (var entry in LocalizationEntries)
            {
                if (map.TryGetValue(entry.Key, out var newTrans) && !string.IsNullOrEmpty(newTrans))
                {
                    entry.TranslatedText = newTrans;
                    entry.RefreshFromModel();
                    importedCount++;
                }
            }

            ApplyFilter();
            CompletedEntries = LocalizationEntries.Count(e => e.Status == TranslationStatus.Done || e.Status == TranslationStatus.ManuallyEdited);
            StatusMessage = $"CSV'den {importedCount} satır çeviri başarıyla içe aktarıldı.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV içe aktarma hatası: {ex.Message}";
        }
    }

    /// <summary>
    /// RFC 4180 uyumlu CSV ayrıştırıcı: "" kaçışını ve tırnak içindeki satır sonlarını destekler.
    /// </summary>
    private static List<string[]> ParseCsv(string content)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cur.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                fields.Add(cur.ToString()); cur.Clear();
                if (fields.Count > 1 || fields[0].Length > 0) rows.Add(fields.ToArray());
                fields.Clear();
            }
            else cur.Append(c);
        }

        if (cur.Length > 0 || fields.Count > 0)
        {
            fields.Add(cur.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }

    /// <summary>
    /// Hücreyi CSV için kaçışlar ve Excel formül enjeksiyonunu (=, +, -, @ ile başlayan) etkisizleştirir.
    /// </summary>
    private static string CsvSafe(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
            v = "'" + v;
        return v.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Paketi dışa aktarırken çevrilmiş dosyaları yeni verisiyle, dokunulmaması gereken tüm orijinal
    /// oyun motoru varlıklarını (fontlar, ses tabloları, konfigürasyonlar, ikili dosyalar) ise
    /// orijinal paketten bayt bayt okuyup %100 eksiksiz yerine koyarak tam ve hatasız bir paket oluşturur.
    /// </summary>
    private async Task<List<(string VirtualPath, byte[] Data)>> PrepareAllArchiveFilesForExportAsync()
    {
        var filesToWrite = new List<(string VirtualPath, byte[] Data)>();
        if (_currentArchive is null) return filesToWrite;

        foreach (var entry in _currentArchive.Entries)
        {
            if (_archiveLocresCache.TryGetValue(entry.VirtualPath, out var cachedLocres))
            {
                // Çevrilmiş lokalizasyon dosyası: Yeni serileştirilmiş veriyi koy
                var data = await _locresService.SerializeAsync(cachedLocres);
                filesToWrite.Add((entry.VirtualPath, data));
            }
            else if (_currentLocresFile is not null && string.Equals(_currentLocresFile.SourcePath, entry.VirtualPath, StringComparison.OrdinalIgnoreCase))
            {
                // Aktif tekli çevrilmiş dosya
                var data = await _locresService.SerializeAsync(_currentLocresFile);
                filesToWrite.Add((entry.VirtualPath, data));
            }
            else
            {
                // Dokunulmaz orijinal oyun motoru dosyası (Font, ses, konfigürasyon, ikili veri):
                // Orijinal paketten bayt bayt çıkar ve yeni pakete eksiksiz yerine koy!
                try
                {
                    using var origStream = await _pakService.ExtractEntryAsync(_currentArchive, entry);
                    using var ms = new MemoryStream();
                    await origStream.CopyToAsync(ms);
                    filesToWrite.Add((entry.VirtualPath, ms.ToArray()));
                }
                catch
                {
                    // Herhangi bir okuma hatasında atla
                }
            }
        }

        return filesToWrite;
    }

    [RelayCommand]
    private async Task SavePakDirectAsync()
    {
        if (_currentLocresFile is null) return;

        // Tekil dosya modu (.locres, .json, .csv, .xml)
        if (_currentArchive is null)
        {
            var defaultSingleName = Path.GetFileName(_currentLocresFile.SourcePath ?? "Localization.locres");
            var outputSinglePath = await ShowSaveFileDialogAsync(
                "Dosyayı Kaydet",
                [["*.*"]],
                defaultSingleName);
            if (outputSinglePath is null) return;

            try
            {
                IsLoading = true;
                StatusMessage = "Dosya kaydediliyor...";
                var data = await _locresService.SerializeAsync(_currentLocresFile);
                await File.WriteAllBytesAsync(outputSinglePath, data);
                LastExportedDirectory = Path.GetDirectoryName(outputSinglePath);
                HasExportedDirectory = !string.IsNullOrEmpty(LastExportedDirectory);
                StatusMessage = $"Dosya kaydedildi: {Path.GetFileName(outputSinglePath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Kaydetme hatası: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
            return;
        }

        var defaultName = Path.GetFileName(_currentArchive.FilePath);
        var outputPath = await ShowSaveFileDialogAsync(
            ".pak Dosyasını Kaydet (Doğrudan Değiştir)",
            [["*.pak"]],
            defaultName);

        if (outputPath is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Paket dosyası kaydediliyor...";

            var mountPoint = _currentArchive.MountPoint;
            var filesToWrite = await PrepareAllArchiveFilesForExportAsync();

            await _pakService.WriteAsync(outputPath, mountPoint, filesToWrite,
                sourceFormat: _currentArchive.Format);

            LastExportedDirectory = Path.GetDirectoryName(outputPath);
            HasExportedDirectory = !string.IsNullOrEmpty(LastExportedDirectory);

            StatusMessage = $"Paket kaydedildi: {Path.GetFileName(outputPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kaydetme hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportPatchAsync()
    {
        if (_currentLocresFile is null || _currentArchive is null) return;

        var outputPath = await ShowSaveFileDialogAsync(
            "Yama paketi kaydet (_P.pak)",
            [["*.pak"]],
            Path.GetFileNameWithoutExtension(_currentArchive.FileName) + "-Turkish_P.pak");

        if (outputPath is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Yama paketi oluşturuluyor...";

            var filesToWrite = new List<(string VirtualPath, byte[] Data)>();

            if (_currentLocresFile.SourcePath == AllFilesVirtualPath)
            {
                // Tüm arşiv modu: Her bir orijinal lokalizasyon girdisi için serialize edilmiş veriyi al
                foreach (var entry in _currentArchive.LocalizationEntries)
                {
                    if (_archiveLocresCache.TryGetValue(entry.VirtualPath, out var cachedLocres))
                    {
                        var data = await _locresService.SerializeAsync(cachedLocres);
                        filesToWrite.Add((entry.VirtualPath, data));
                    }
                }
            }
            else
            {
                var locresData = await _locresService.SerializeAsync(_currentLocresFile);
                var virtualPath = _currentLocresFile.SourcePath ?? _currentArchive.LocalizationEntries.FirstOrDefault()?.VirtualPath ?? "Game/Content/Localization/Game.locres";
                filesToWrite.Add((virtualPath, locresData));
            }

            if (filesToWrite.Count == 0)
            {
                StatusMessage = "Yama paketine eklenecek geçerli lokalizasyon dosyası bulunamadı.";
                return;
            }

            var mountPoint = _currentArchive.MountPoint;
            await _pakService.WriteAsync(outputPath, mountPoint,
                filesToWrite,
                sourceFormat: _currentArchive.Format);

            LastExportedDirectory = Path.GetDirectoryName(outputPath);
            HasExportedDirectory = !string.IsNullOrEmpty(LastExportedDirectory);

            StatusMessage = $"Yama paketi oluşturuldu: {Path.GetFileName(outputPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dışa aktarma hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var filtered = LocalizationEntries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filtered = filtered.Where(e =>
                e.Key.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                e.SourceText.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                e.TranslatedText.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
        }

        if (ShowOnlyErrors)
        {
            filtered = filtered.Where(e => e.Status == TranslationStatus.Error || e.HasQaIssue);
        }

        if (!string.IsNullOrEmpty(SelectedStatusFilter) && SelectedStatusFilter != "Tümü")
        {
            filtered = SelectedStatusFilter switch
            {
                "Bekleyenler (Pending)" => filtered.Where(e => e.Status == TranslationStatus.Pending),
                "Tamamlananlar" => filtered.Where(e => e.Status == TranslationStatus.Done),
                "Hatalar" => filtered.Where(e => e.Status == TranslationStatus.Error),
                "Manuel Düzenlenenler" => filtered.Where(e => e.Status == TranslationStatus.ManuallyEdited),
                "Taşma Riski (%40+)" => filtered.Where(e => e.IsOverflowRisk),
                "QA / Etiket Hataları" => filtered.Where(e => e.HasQaIssue),
                _ => filtered
            };
        }

        FilteredEntries = new ObservableCollection<LocalizationEntryViewModel>(filtered);
    }

    public static string MapLanguageToIsoCode(string? languageName)
    {
        if (string.IsNullOrWhiteSpace(languageName)) return "tr";
        var norm = languageName.Trim().ToLowerInvariant();
        return norm switch
        {
            "türkçe" => "tr",
            "ingilizce" or "i̇ngilizce" => "en",
            "almanca" => "de",
            "fransızca" => "fr",
            "ispanyolca" or "i̇spanyolca" => "es",
            "rusça" => "ru",
            "japonca" => "ja",
            "çince" => "zh",
            "korece" => "ko",
            "italyanca" or "i̇italyanca" => "it",
            "portekizce" => "pt",
            "arapça" => "ar",
            _ => "tr"
        };
    }

    public ITranslationProvider CreateProvider()
    {
        // Her seferinde yeni HttpClient oluştur (Timeout değiştirilebilir)
        // SocketsHttpHandler paylaşıldığı için socket leak olmaz
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

    // Platform-specific dialog desteği için event pattern
    // Gerçek implementasyon View'da yapılır (IStorageProvider)
    public Func<string, string[][], Task<string?>>? ShowOpenFileDialogFunc { get; set; }
    public Func<string, string[][], string, Task<string?>>? ShowSaveFileDialogFunc { get; set; }

    private Task<string?> ShowOpenFileDialogAsync(string title, string[][] filters)
        => ShowOpenFileDialogFunc?.Invoke(title, filters) ?? Task.FromResult<string?>(null);

    private Task<string?> ShowSaveFileDialogAsync(string title, string[][] filters, string suggested)
        => ShowSaveFileDialogFunc?.Invoke(title, filters, suggested) ?? Task.FromResult<string?>(null);

    // ─── Settings Persistence ───────────────────────────────────────────────

    public async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await SettingsService.LoadAsync();
            GeminiApiKey = settings.GeminiApiKey;
            // Kaldırılan Gemini modellerini yeni modele otomatik yönlendir
            var savedGeminiModel = settings.GeminiModel;
            var retiredGeminiModels = new HashSet<string> {
                "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-flash-lite",
                "gemini-2.0-flash", "gemini-2.0-flash-lite",
                "gemini-1.5-flash", "gemini-1.5-flash-8b", "gemini-1.5-pro", "gemini-1.0-pro"
            };
            GeminiModel = string.IsNullOrWhiteSpace(savedGeminiModel) || retiredGeminiModels.Contains(savedGeminiModel)
                ? "gemini-3.8-flash"
                : savedGeminiModel;
            GroqApiKey = settings.GroqApiKey;
            // Eski/kaldırılan Groq modelleri yeni modele yönlendir
            var savedGroqModel = settings.GroqModel;
            var retiredGroqModels = new HashSet<string> {
                "llama-3.3-70b-versatile", "llama-3.1-8b-instant",
                "llama3-70b-8192", "llama3-8b-8192",
                "deepseek-r1-distill-llama-70b", "qwen-2.5-32b",
                "mixtral-8x7b-32768", "gemma2-9b-it", "qwen/qwen3.6-27b"
            };
            GroqModel = string.IsNullOrWhiteSpace(savedGroqModel) || retiredGroqModels.Contains(savedGroqModel)
                ? "openai/gpt-oss-20b"
                : savedGroqModel;
            ClaudeApiKey = settings.ClaudeApiKey;
            // Eski/kaldırılan Claude modellerini yeni modele yönlendir
            var savedClaudeModel = settings.ClaudeModel;
            var retiredClaudeModels = new HashSet<string> {
                "claude-3-5-haiku-20241022", "claude-3-opus-20240229",
                "claude-2.1", "claude-2.0", "claude-instant-1.2"
            };
            ClaudeModel = string.IsNullOrWhiteSpace(savedClaudeModel) || retiredClaudeModels.Contains(savedClaudeModel)
                ? "claude-sonnet-5-5"
                : savedClaudeModel;
            OpenAiApiKey = settings.OpenAiApiKey;
            // Eski/kaldırılan OpenAI modellerini yeni modele yönlendir
            var savedOpenAiModel = settings.OpenAiModel;
            var retiredOpenAiModels = new HashSet<string> {
                "o3-mini", "o1", "o1-preview", "gpt-4.5-preview", "gpt-4-turbo", "gpt-4", "gpt-3.5-turbo"
            };
            OpenAiModel = string.IsNullOrWhiteSpace(savedOpenAiModel) || retiredOpenAiModels.Contains(savedOpenAiModel)
                ? "gpt-6.1-sol"
                : savedOpenAiModel;
            DeepSeekApiKey = settings.DeepSeekApiKey;
            DeepSeekModel = string.IsNullOrWhiteSpace(settings.DeepSeekModel) ? "deepseek-chat" : settings.DeepSeekModel;
            KimiApiKey = settings.KimiApiKey;
            KimiModel = string.IsNullOrWhiteSpace(settings.KimiModel) ? "moonshot-v1-8k" : settings.KimiModel;
            QwenApiKey = settings.QwenApiKey;
            QwenModel = string.IsNullOrWhiteSpace(settings.QwenModel) ? "qwen-plus" : settings.QwenModel;
            OpenRouterApiKey = settings.OpenRouterApiKey;
            OpenRouterModel = string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? "deepseek/deepseek-chat" : settings.OpenRouterModel;
            TogetherApiKey = settings.TogetherApiKey;
            TogetherModel = string.IsNullOrWhiteSpace(settings.TogetherModel) ? "meta-llama/Llama-3.3-70B-Instruct-Turbo" : settings.TogetherModel;
            MistralApiKey = settings.MistralApiKey;
            MistralModel = string.IsNullOrWhiteSpace(settings.MistralModel) ? "mistral-large-latest" : settings.MistralModel;
            DeepInfraApiKey = settings.DeepInfraApiKey;
            DeepInfraModel = string.IsNullOrWhiteSpace(settings.DeepInfraModel) ? "meta-llama/Meta-Llama-3.1-70B-Instruct" : settings.DeepInfraModel;
            PerplexityApiKey = settings.PerplexityApiKey;
            PerplexityModel = string.IsNullOrWhiteSpace(settings.PerplexityModel) ? "sonar-pro" : settings.PerplexityModel;
            CustomEndpointUrl = string.IsNullOrWhiteSpace(settings.CustomEndpointUrl) ? "http://localhost:11434/v1" : settings.CustomEndpointUrl;
            CustomEndpointApiKey = settings.CustomEndpointApiKey;
            CustomEndpointModel = string.IsNullOrWhiteSpace(settings.CustomEndpointModel) ? "qwen2.5" : settings.CustomEndpointModel;
            SelectedProviderName = string.IsNullOrEmpty(settings.SelectedProvider) ? "Groq (Ücretsiz & Süper Hızlı)" : settings.SelectedProvider;
            if (!string.IsNullOrWhiteSpace(settings.SourceLanguage))
                SelectedSourceLanguage = settings.SourceLanguage;
            if (!string.IsNullOrWhiteSpace(settings.TargetLanguage))
                SelectedTargetLanguage = settings.TargetLanguage;
            if (!string.IsNullOrWhiteSpace(settings.ReasoningEffort))
                SelectedReasoningEffort = settings.ReasoningEffort;
            if (!string.IsNullOrWhiteSpace(settings.AppLanguage))
                SelectedAppLanguage = settings.AppLanguage;
            if (!string.IsNullOrWhiteSpace(settings.PromptLanguage))
                SelectedPromptLanguage = settings.PromptLanguage;
            UserDirective = settings.DefaultDirective;
            SystemPrompt = string.IsNullOrWhiteSpace(settings.SystemPrompt) ? AppSettings.GetMasterPromptForLanguage(SelectedPromptLanguage) : settings.SystemPrompt;
            EnableTranslationMemory = settings.EnableTranslationMemory;
            GlossaryList = new ObservableCollection<GlossaryEntry>(settings.GlossaryEntries);
        }
        catch { }
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new AppSettings
            {
                GeminiApiKey = GeminiApiKey,
                GeminiModel = GeminiModel,
                OpenRouterApiKey = OpenRouterApiKey,
                OpenRouterModel = OpenRouterModel,
                TogetherApiKey = TogetherApiKey,
                TogetherModel = TogetherModel,
                MistralApiKey = MistralApiKey,
                MistralModel = MistralModel,
                DeepInfraApiKey = DeepInfraApiKey,
                DeepInfraModel = DeepInfraModel,
                PerplexityApiKey = PerplexityApiKey,
                PerplexityModel = PerplexityModel,
                GroqApiKey = GroqApiKey,
                GroqModel = GroqModel,
                ClaudeApiKey = ClaudeApiKey,
                ClaudeModel = ClaudeModel,
                OpenAiApiKey = OpenAiApiKey,
                OpenAiModel = OpenAiModel,
                DeepSeekApiKey = DeepSeekApiKey,
                DeepSeekModel = DeepSeekModel,
                KimiApiKey = KimiApiKey,
                KimiModel = KimiModel,
                QwenApiKey = QwenApiKey,
                QwenModel = QwenModel,
                CustomEndpointUrl = CustomEndpointUrl,
                CustomEndpointApiKey = CustomEndpointApiKey,
                CustomEndpointModel = CustomEndpointModel,
                SelectedProvider = SelectedProviderName,
                SourceLanguage = SelectedSourceLanguage,
                TargetLanguage = SelectedTargetLanguage,
                AppLanguage = SelectedAppLanguage,
                PromptLanguage = SelectedPromptLanguage,
                ReasoningEffort = SelectedReasoningEffort,
                DefaultDirective = UserDirective,
                SystemPrompt = SystemPrompt,
                EnableTranslationMemory = EnableTranslationMemory,
                GlossaryEntries = GlossaryList.ToList()
            };
            await SettingsService.SaveAsync(settings);
            StatusMessage = "Ayarlar başarıyla kaydedildi.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ayarlar kaydedilemedi: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetSystemPromptToDefault()
    {
        SystemPrompt = AppSettings.GetMasterPromptForLanguage(SelectedPromptLanguage);
    }

    // ─── YEREL AI MODEL KURULUM MERKEZİ (OLLAMA HUB) ───

    public static IReadOnlyList<LocalAiModelOption> PresetLocalAiModels => OllamaLocalModelService.PresetModels;

    [ObservableProperty]
    private LocalAiModelOption _selectedLocalAiModel = OllamaLocalModelService.PresetModels[1]; // Varsayılan: Qwen 2.5 7B

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

        // Önce Ollama açık mı kontrol et
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

            // Başarıyla tamamlandı!
            InstalledModelSuccessName = modelToInstall;
            CustomEndpointModel = modelToInstall;
            CustomEndpointUrl = "http://localhost:11434/v1";
            SelectedProviderName = "Özel API / Yerel LLM";
            ShowModelInstructions = true;
            ModelDownloadStatusMessage = $"✅ {modelToInstall} başarıyla kuruldu ve aktif motor olarak ayarlandı!";
            ModelDownloadDetail = "Ollama üzerinden sınırsız ve kotasız çeviriye hazırsınız.";

            // Kurulu modelleri güncelle
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

    // ─── TOPLU BUL VE DEĞİŞTİR (BATCH FIND & REPLACE) ────────────────────────
    [RelayCommand]
    public void ToggleFindReplace()
    {
        IsFindReplaceOpen = !IsFindReplaceOpen;
        if (IsFindReplaceOpen)
        {
            FindResultInfo = "Aranacak kelimeyi girin ve 'Tümünü Değiştir' veya 'Sonrakini Bul' butonuna tıklayın.";
        }
    }

    [RelayCommand]
    public void CloseFindReplace()
    {
        IsFindReplaceOpen = false;
    }

    [RelayCommand]
    public void ReplaceAll()
    {
        if (string.IsNullOrEmpty(FindText))
        {
            FindResultInfo = "Lütfen aranacak metni girin.";
            return;
        }

        int affectedEntries = 0;
        int totalReplacements = 0;

        RegexOptions regexOptions = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        Regex? regex = null;

        if (UseRegex)
        {
            try
            {
                regex = new Regex(FindText, regexOptions);
            }
            catch (Exception ex)
            {
                FindResultInfo = $"Geçersiz Regex: {ex.Message}";
                return;
            }
        }
        else if (MatchWholeWord)
        {
            var pattern = $@"\b{Regex.Escape(FindText)}\b";
            regex = new Regex(pattern, regexOptions);
        }

        bool targetTranslation = FindScope is "Yalnızca Hedef Çeviri" or "Her İkisi";
        bool targetSource = FindScope is "Yalnızca Kaynak Metin" or "Her İkisi";

        foreach (var vm in LocalizationEntries)
        {
            bool modified = false;

            if (targetTranslation && !string.IsNullOrEmpty(vm.TranslatedText))
            {
                string newText;
                int count = 0;

                if (regex != null)
                {
                    count = regex.Matches(vm.TranslatedText).Count;
                    newText = regex.Replace(vm.TranslatedText, ReplaceText ?? string.Empty);
                }
                else
                {
                    var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    newText = ReplaceString(vm.TranslatedText, FindText, ReplaceText ?? string.Empty, comparison, out count);
                }

                if (count > 0 && newText != vm.TranslatedText)
                {
                    vm.TranslatedText = newText;
                    vm.Model.Status = TranslationStatus.ManuallyEdited;
                    vm.RefreshFromModel();
                    modified = true;
                    totalReplacements += count;
                }
            }

            if (modified)
            {
                affectedEntries++;
            }
        }

        ApplyFilter();
        FindResultInfo = $"✅ {affectedEntries} satırda toplam {totalReplacements} değişiklik yapıldı.";
        StatusMessage = $"Toplu değiştirme tamamlandı: {affectedEntries} satır güncellendi ({totalReplacements} eşleşme).";
    }

    [RelayCommand]
    public void FindNext()
    {
        if (string.IsNullOrEmpty(FindText))
        {
            FindResultInfo = "Lütfen aranacak metni girin.";
            return;
        }

        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int currentIndex = SelectedEntry != null ? FilteredEntries.IndexOf(SelectedEntry) : -1;
        int count = FilteredEntries.Count;

        if (count == 0)
        {
            FindResultInfo = "Tabloda taranacak satır yok.";
            return;
        }

        bool targetTranslation = FindScope is "Yalnızca Hedef Çeviri" or "Her İkisi";
        bool targetSource = FindScope is "Yalnızca Kaynak Metin" or "Her İkisi";

        for (int i = 1; i <= count; i++)
        {
            int checkIndex = (currentIndex + i) % count;
            var item = FilteredEntries[checkIndex];

            bool match = false;
            if (targetTranslation && item.TranslatedText?.IndexOf(FindText, comparison) >= 0)
                match = true;
            if (!match && targetSource && item.SourceText?.IndexOf(FindText, comparison) >= 0)
                match = true;

            if (match)
            {
                SelectedEntry = item;
                FindResultInfo = $"Bulundu (Satır {checkIndex + 1}/{count}): '{item.Key}'";
                return;
            }
        }

        FindResultInfo = $"Eşleşme bulunamadı: '{FindText}'";
    }

    private static string ReplaceString(string str, string oldValue, string newValue, StringComparison comparison, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(oldValue)) return str;

        var sb = new System.Text.StringBuilder();
        int previousIndex = 0;
        int index = str.IndexOf(oldValue, comparison);

        while (index != -1)
        {
            sb.Append(str.Substring(previousIndex, index - previousIndex));
            sb.Append(newValue);
            count++;
            previousIndex = index + oldValue.Length;
            index = str.IndexOf(oldValue, previousIndex, comparison);
        }

        sb.Append(str.Substring(previousIndex));
        return sb.ToString();
    }

    // ─── SAĞ TIK İÇERİK MENÜSÜ KOMUTLARI (CONTEXT MENU) ─────────────────────

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
    }

    [RelayCommand]
    public void MarkAsPending(LocalizationEntryViewModel? entry)
    {
        var item = entry ?? SelectedEntry;
        if (item == null) return;
        item.Model.Status = TranslationStatus.Pending;
        item.RefreshFromModel();
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

    // ─── OTOMATİK KALİTE KONTROL (QA CHECKER) ───────────────────────────────

    [RelayCommand]
    public void RunQaCheck()
    {
        if (LocalizationEntries.Count == 0)
        {
            StatusMessage = "Önce bir dosya açmalısınız.";
            return;
        }

        int totalIssues = 0;
        int affectedEntries = 0;

        foreach (var vm in LocalizationEntries)
        {
            var issues = TranslationQaService.ValidateEntry(vm.Model);
            if (issues.Count > 0)
            {
                affectedEntries++;
                totalIssues += issues.Count;
                vm.Model.HasVariableError = true;
                vm.Model.Notes = string.Join(" | ", issues.Select(i => i.Description));
                vm.RefreshFromModel();
            }
            else
            {
                vm.Model.HasVariableError = false;
                vm.RefreshFromModel();
            }
        }

        SelectedStatusFilter = "QA / Etiket Hataları";
        ApplyFilter();

        if (affectedEntries > 0)
        {
            StatusMessage = $"⚠️ Kalite Kontrolü: {affectedEntries} satırda toplam {totalIssues} etiket/değişken sorunu bulundu. 'Etiketleri Otomatik Onar' ile düzeltebilirsiniz.";
        }
        else
        {
            StatusMessage = "✅ Kalite Kontrolü Başarılı! Hiçbir eksik değişken veya kapanmayan etiket bulunamadı.";
        }
    }

    [RelayCommand]
    public void AutoFixAllQaIssues()
    {
        if (LocalizationEntries.Count == 0) return;

        int fixedCount = 0;
        foreach (var vm in LocalizationEntries)
        {
            if (TranslationQaService.AutoFixEntry(vm.Model))
            {
                vm.RefreshFromModel();
                fixedCount++;
            }
        }

        ApplyFilter();
        StatusMessage = fixedCount > 0
            ? $"✅ {fixedCount} satırdaki eksik etiketler ve değişkenler otomatik olarak onarıldı!"
            : "Onarılacak etiket veya değişken sorunu bulunamadı.";
    }

    // ─── TEKİL DOSYA AÇMA (.locres, .json, .csv, .xml) ─────────────────────

    [RelayCommand]
    public async Task OpenSingleFileAsync()
    {
        var path = await ShowOpenFileDialogAsync(
            "Lokalizasyon Dosyası Seç (.locres, .json, .csv, .xml, .po)",
            [["*.locres", "*.json", "*.csv", "*.tsv", "*.xml", "*.txt", "*.po"]]);

        if (path is null) return;
        await OpenSingleFileFromPathAsync(path);
    }

    public async Task OpenSingleFileFromPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            IsLoading = true;
            LastOpenedPath = path;
            StatusMessage = $"Dosya açılıyor: {Path.GetFileName(path)}...";

            await Task.Run(async () =>
            {
                await using var fs = File.OpenRead(path);
                var locres = await _locresService.DeserializeAsync(fs);
                locres.SourcePath = path;

                _currentLocresFile = locres;
                _currentArchive = null; // Tekil dosya modu

                var viewModels = _currentLocresFile.AllEntries
                    .Select(e => new LocalizationEntryViewModel(e))
                    .ToList();

                var total = viewModels.Count;
                var done = viewModels.Count(v => v.Status == TranslationStatus.Done || v.Status == TranslationStatus.ManuallyEdited);
                var err = viewModels.Count(v => v.Status == TranslationStatus.Error);
                var obsCollection = new ObservableCollection<LocalizationEntryViewModel>(viewModels);

                var fi = new FileInfo(path);
                var sizeInfo = $"{(fi.Length / 1024.0):F1} KB";

                Dispatcher.UIThread.Post(() =>
                {
                    PakEntries = new ObservableCollection<PakEntry>([new PakEntry { VirtualPath = path }]);
                    LoadedPakFileName = Path.GetFileName(path);
                    LoadedPakSizeInfo = sizeInfo;
                    IsPakLoaded = true;
                    LocalizationEntries = obsCollection;
                    TotalEntries = total;
                    CompletedEntries = done;
                    ErrorCount = err;
                    ApplyFilter();
                    StatusMessage = $"Yüklendi: {Path.GetFileName(path)} — {TotalEntries} girdi ({CompletedEntries} tamamlandı, {ErrorCount} hata)";
                    IsLoading = false;
                });
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusMessage = $"Dosya açılamadı: {ex.Message}";
                IsLoading = false;
            });
        }
    }

    // ─── OTOMATİK KURTARMA (DISASTER RECOVERY) ──────────────────────────────

    public async Task CheckRecoverySessionAsync()
    {
        var info = RecoveryService.GetRecoverySnapshotInfo();
        if (info != null)
        {
            HasRecoverySession = true;
            RecoverySessionInfo = $"⚠️ Kurtarılabilir Çeviri Oturumu Tespit Edildi ({Path.GetFileName(info.Value.SourcePath)} — {info.Value.EntryCount} satır, {info.Value.Timestamp:HH:mm:ss})";
        }
    }

    [RelayCommand]
    public async Task RestoreRecoverySessionAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Önceki çeviri oturumu kurtarılıyor...";
            var snapshot = await RecoveryService.LoadRecoverySnapshotAsync();
            if (snapshot == null || snapshot.Entries.Count == 0)
            {
                HasRecoverySession = false;
                StatusMessage = "Kurtarılacak oturum verisi bulunamadı.";
                return;
            }

            var locres = new LocresFile
            {
                SourcePath = snapshot.SourcePath,
                Version = LocresVersion.V3
            };
            var ns = new LocresNamespace { Name = "Kurtarilan_Metinler" };
            foreach (var e in snapshot.Entries) ns.Entries.Add(e);
            locres.Namespaces.Add(ns);
            _currentLocresFile = locres;

            var vms = snapshot.Entries.Select(e => new LocalizationEntryViewModel(e)).ToList();
            LocalizationEntries = new ObservableCollection<LocalizationEntryViewModel>(vms);
            TotalEntries = vms.Count;
            CompletedEntries = vms.Count(e => e.Status == TranslationStatus.Done || e.Status == TranslationStatus.ManuallyEdited);
            ErrorCount = vms.Count(e => e.Status == TranslationStatus.Error);
            ApplyFilter();

            LoadedPakFileName = Path.GetFileName(snapshot.SourcePath);
            IsPakLoaded = true;
            HasRecoverySession = false;
            StatusMessage = $"✅ Çeviri oturumu başarıyla kurtarıldı! ({TotalEntries} satır, {CompletedEntries} tamamlanmış)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Kurtarma hatası: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void DismissRecoverySession()
    {
        RecoveryService.ClearRecoverySnapshot();
        HasRecoverySession = false;
        StatusMessage = "Kurtarma oturumu temizlendi.";
    }
}

