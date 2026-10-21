using System.Collections;
using System.Windows;
using System.Windows.Markup;
using SolidRecords.Core;
using SolidRecords.Core.Localization;

namespace SolidRecords.App.Presentation;

public sealed class LocalizationDictionary : ResourceDictionary
{
    public LocalizationDictionary() => Refresh();

    private void Refresh()
    {
        // DatePicker creates its popup calendar outside the window's language inheritance.
        this["Localization.Language"] = XmlLanguage.GetLanguage(Strings.Culture.IetfLanguageTag);
        this["Localization.FirstDayOfWeek"] = Strings.Culture.DateTimeFormat.FirstDayOfWeek;
        var resources = Strings.Resources.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true)!;
        foreach (DictionaryEntry resource in resources)
            this["Text." + resource.Key] = Strings.Get((string)resource.Key);
    }

    public static void Apply(ApplicationLanguage language)
    {
        Strings.Apply(language);
        LocalizedText.Instance.RefreshLanguage();
        if (Application.Current is not { } app) return;
        foreach (var dictionary in app.Resources.MergedDictionaries.OfType<LocalizationDictionary>()) dictionary.Refresh();
        // Language controls WPF date editors, calendar headings and binding formatting.
        foreach (Window window in app.Windows)
        {
            window.Language = XmlLanguage.GetLanguage(Strings.Culture.IetfLanguageTag);
            if (window.DataContext is ObservableObject model) model.RefreshLanguage();
        }
    }
}

// DataGridColumn has no resource inheritance context; bind its headers explicitly.
public sealed class LocalizedText : ObservableObject
{
    public static LocalizedText Instance { get; } = new();
    public string this[string key] => Strings.Get(key);
}
