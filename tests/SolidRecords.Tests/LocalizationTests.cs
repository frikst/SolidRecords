using System.Collections;
using System.Globalization;
using System.Text.Json;
using SolidRecords.Core;
using SolidRecords.Core.Localization;
using SolidRecords.Storage;
using Xunit;

namespace SolidRecords.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData(ApplicationLanguage.System, "sk-SK", "sk-SK")]
    [InlineData(ApplicationLanguage.System, "en-GB", "en-US")]
    [InlineData(ApplicationLanguage.System, "de-DE", "en-US")]
    [InlineData(ApplicationLanguage.English, "sk-SK", "en-US")]
    [InlineData(ApplicationLanguage.Slovak, "en-US", "sk-SK")]
    public void LanguageFollowsEnvironmentWithEnglishFallback(ApplicationLanguage language, string environment, string expected) =>
        Assert.Equal(expected, Strings.Resolve(language, CultureInfo.GetCultureInfo(environment)).Name);

    [Fact]
    public void OlderSettingsFollowEnvironmentAndInvalidLanguageIsRejected()
    {
        Assert.Equal(ApplicationLanguage.System, JsonSerializer.Deserialize<ApplicationSettings>("{}")!.Language);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(Language: (ApplicationLanguage)99).Validate());
    }

    [Fact]
    public void BothResourceSetsContainEveryKeyAndMatchingFormatArguments()
    {
        var english = Strings.Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var slovak = Strings.Resources.GetResourceSet(CultureInfo.GetCultureInfo("sk"), true, false)!;
        Assert.Equal(english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order(),
            slovak.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order());
        foreach (DictionaryEntry entry in english)
        {
            var translation = Assert.IsType<string>(slovak.GetString((string)entry.Key));
            Assert.False(string.IsNullOrWhiteSpace(translation));
            Assert.Equal(System.Text.CompositeFormat.Parse((string)entry.Value!).MinimumArgumentCount,
                System.Text.CompositeFormat.Parse(translation).MinimumArgumentCount);
        }
        Assert.Equal("Save", english.GetString("Save"));
        Assert.Equal("Uložiť", slovak.GetString("Save"));
    }

    [Fact]
    public async Task LanguagePreferenceRoundTripsThroughIsolatedDatabase()
    {
        var directory = Path.GetFullPath(Path.Combine("artifacts", "localization-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var store = new SqliteActivityStore(Path.Combine(directory, "activity.db"));
        await store.InitializeAsync();
        foreach (var language in Enum.GetValues<ApplicationLanguage>())
        {
            var settings = new ApplicationSettings(Language: language);
            await store.SaveSettingsAsync(settings);
            Assert.Equal(settings, await store.ReadSettingsAsync());
        }
    }
}
