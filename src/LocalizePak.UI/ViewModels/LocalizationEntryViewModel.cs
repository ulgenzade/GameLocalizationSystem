using CommunityToolkit.Mvvm.ComponentModel;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
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
        _translatedText = model.TranslatedText ?? string.Empty;
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
