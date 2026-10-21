using System.Globalization;
using System.Resources;

namespace SolidRecords.Core.Localization;

/// <summary>Shared UI and diagnostic text; no desktop dependencies.</summary>
public static class Strings
{
    public static ResourceManager Resources { get; } = new("SolidRecords.Core.Localization.Strings", typeof(Strings).Assembly);
    private static readonly CultureInfo EnvironmentCulture = CultureInfo.CurrentUICulture;
    private static CultureInfo _culture = Resolve(ApplicationLanguage.System, EnvironmentCulture);
    public static CultureInfo Culture => Volatile.Read(ref _culture);

    public static CultureInfo Resolve(ApplicationLanguage language, CultureInfo environment) =>
        CultureInfo.GetCultureInfo(language switch
        {
            ApplicationLanguage.Slovak => "sk-SK",
            ApplicationLanguage.English => "en-US",
            _ => environment.TwoLetterISOLanguageName == "sk" ? "sk-SK" : "en-US"
        });

    public static void Apply(ApplicationLanguage language)
    {
        var culture = Resolve(language, EnvironmentCulture);
        Volatile.Write(ref _culture, culture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string Get(string key) => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException($"Missing translation: {key}");
    public static string Format(string key, params object?[] arguments) => string.Format(Culture, Get(key), arguments);
}
