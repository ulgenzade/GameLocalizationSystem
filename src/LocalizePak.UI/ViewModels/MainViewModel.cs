using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalizePak.Core.Models;
using LocalizePak.IO;
using LocalizePak.IO.Formats;
using LocalizePak.IO.Helpers;
using LocalizePak.IO.Services;
using LocalizePak.Translation;
using LocalizePak.Translation.Services;

namespace LocalizePak.UI.ViewModels;

/// <summary>
/// Ana pencere ViewModel.
/// Modüler mimari ile kısmi sınıflara (partial classes) ayrılmıştır:
/// - MainViewModel.cs: Çekirdek servisler, ayarlar ve pencere yaşam döngüsü
/// - MainViewModel.Archive.cs: Paket açma, çıkarma, dosya ağacı ve sekmeler (ArchiveWorkspace)
/// - MainViewModel.Translation.cs: AI motoru, batch akışı, buton durumları ve sağlayıcılar (TranslationRun)
/// - MainViewModel.Filter.cs: Arama, durum filtreleri, bul & değiştir ve QA (GridFilter)
/// - MainViewModel.Export.cs: Yama derleme (_P.pak), CSV içe/dışa aktar ve oturum kurtarma (ExportPatch)
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly PakService _pakService = new();
    private readonly UniversalLocalizationService _locresService = new();
    private readonly BatchTranslationOrchestrator _orchestrator = new();

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

    public MainViewModel()
    {
        _ = LoadSettingsAsync();
        _ = CheckRecoverySessionAsync();
    }

    // ─── Temel UI Durum Özellikleri ─────────────────────────────────────────

    [ObservableProperty]
    private string _statusMessage = "Başlamak için bir .pak dosyası açın.";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _aesKey = string.Empty;

    // ─── Dosya İletişim Kutusu Köprüleri (View Bridge) ─────────────────────

    public Func<string, string[][], Task<string?>>? ShowOpenFileDialogFunc { get; set; }
    public Func<string, string[][], string, Task<string?>>? ShowSaveFileDialogFunc { get; set; }

    private Task<string?> ShowOpenFileDialogAsync(string title, string[][] filters)
        => ShowOpenFileDialogFunc?.Invoke(title, filters) ?? Task.FromResult<string?>(null);

    private Task<string?> ShowSaveFileDialogAsync(string title, string[][] filters, string suggested)
        => ShowSaveFileDialogFunc?.Invoke(title, filters, suggested) ?? Task.FromResult<string?>(null);

    // ─── Ayarların Yüklenmesi ve Kaydedilmesi ───────────────────────────────

    public async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await SettingsService.LoadAsync();
            GeminiApiKey = settings.GeminiApiKey;

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
            var savedClaudeModel = settings.ClaudeModel;
            var retiredClaudeModels = new HashSet<string> {
                "claude-3-5-haiku-20241022", "claude-3-opus-20240229",
                "claude-2.1", "claude-2.0", "claude-instant-1.2"
            };
            ClaudeModel = string.IsNullOrWhiteSpace(savedClaudeModel) || retiredClaudeModels.Contains(savedClaudeModel)
                ? "claude-sonnet-5-5"
                : savedClaudeModel;

            OpenAiApiKey = settings.OpenAiApiKey;
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
}
