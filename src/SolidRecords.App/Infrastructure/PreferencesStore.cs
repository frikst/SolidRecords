using System.Text.Json;
using SolidRecords.Core;

namespace SolidRecords.App.Infrastructure;

internal sealed class PreferencesStore(ISettingsStore store, string dataDirectory)
{
    private sealed record LegacyPreferences(bool RecordOnLaunch = true);
    public ApplicationSettings Current { get; private set; } = new();
    public bool RecordOnLaunch => Current.RecordOnLaunch;

    public async Task LoadAsync()
    {
        var saved = await store.ReadSettingsAsync();
        if (saved is not null) { Current = saved; return; }
        var initial = new ApplicationSettings();
        try
        {
            var path = Path.Combine(dataDirectory, "settings.json");
            if (File.Exists(path))
            {
                var legacy = JsonSerializer.Deserialize<LegacyPreferences>(await File.ReadAllTextAsync(path));
                if (legacy is not null) initial = initial with { RecordOnLaunch = legacy.RecordOnLaunch };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        await SaveAsync(initial);
    }

    public async Task SaveAsync(ApplicationSettings settings)
    {
        await store.SaveSettingsAsync(settings);
        Current = settings;
    }
}
