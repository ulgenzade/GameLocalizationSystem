using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace LocalizePak.UI.Localization;

/// <summary>
/// Avalonia XAML içinde {loc:Loc Key} kullanımı sağlayan MarkupExtension.
/// LocalizationService.Instance indexer'ına OneWay binding bağlar; dil değiştiğinde metinler anında güncellenir.
/// </summary>
public class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay
        };
    }
}
