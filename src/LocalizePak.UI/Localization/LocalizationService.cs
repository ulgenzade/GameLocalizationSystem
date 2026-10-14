using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalizePak.UI.Localization.Languages;

namespace LocalizePak.UI.Localization;

/// <summary>
/// Avalonia UI için dinamik çok dilli (Multilingual) yerelleştirme servisi.
/// Desteklenen Diller: Türkçe (tr), English (en), Русский (ru), 简体中文 (zh).
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly string[] AllKnownKeys = StringsTr.Map.Keys
        .Concat(StringsEn.Map.Keys)
        .Concat(StringsRu.Map.Keys)
        .Concat(StringsZh.Map.Keys)
        .Distinct()
        .ToArray();

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private string _currentLanguage = "tr";
    private IReadOnlyDictionary<string, string> _currentDictionary = StringsTr.Map;

    public string CurrentLanguage => _currentLanguage;

    public string this[string key] => Get(key);

    private LocalizationService()
    {
        SetLanguage("Türkçe");
    }

    /// <summary>
    /// Dil seçeneğini günceller ("Türkçe", "English", "Русский", "简体中文" veya "tr", "en", "ru", "zh").
    /// </summary>
    public void SetLanguage(string? lang, bool force = false)
    {
        var norm = NormalizeLanguageCode(lang);
        if (!force && _currentLanguage == norm && _currentDictionary != null)
            return;

        _currentLanguage = norm;
        _currentDictionary = norm switch
        {
            "en" => StringsEn.Map,
            "ru" => StringsRu.Map,
            "zh" => StringsZh.Map,
            _ => StringsTr.Map
        };

        void NotifyChanges()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            foreach (var k in AllKnownKeys)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{k}]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"[{k}]"));
            }
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        // Avalonia Application başlatılmamışsa (örneğin test ortamında) veya zaten UI thread'deysek doğrudan çalıştır
        if (Avalonia.Application.Current == null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            NotifyChanges();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(NotifyChanges);
        }
    }

    /// <summary>
    /// Belirtilen anahtarın metnini döndürür. Bulunamazsa fallback uygular.
    /// </summary>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        if (_currentDictionary.TryGetValue(key, out var val))
            return val;

        // Fallback: İngilizce veya Türkçe
        if (StringsEn.Map.TryGetValue(key, out var enVal))
            return enVal;

        if (StringsTr.Map.TryGetValue(key, out var trVal))
            return trVal;

        return key;
    }

    /// <summary>
    /// Biçimlendirilmiş metin döndürür (string.Format).
    /// </summary>
    public string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    public static string Tr(string key) => Instance.Get(key);
    public static string Tr(string key, params object[] args) => Instance.Format(key, args);

    public static string NormalizeLanguageCode(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "tr";
        var l = lang.Trim().ToLowerInvariant();
        if (l is "en" or "eng" or "english" or "ingilizce") return "en";
        if (l is "ru" or "rus" or "русский" or "rusça") return "ru";
        if (l is "zh" or "zh-cn" or "zh-hans" or "chinese" or "çince" or "简体中文") return "zh";
        return "tr";
    }

    public static string ToDisplayName(string langCode)
    {
        return NormalizeLanguageCode(langCode) switch
        {
            "en" => "English",
            "ru" => "Русский",
            "zh" => "简体中文",
            _ => "Türkçe"
        };
    }
}
