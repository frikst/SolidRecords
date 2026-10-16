using System.Text.Json;

namespace SolidRecords.App.Infrastructure;

internal sealed class PreferencesStore(string dataDirectory)
{
    private sealed record Preferences(bool RecordOnLaunch = true);
    private readonly string _path = Path.Combine(dataDirectory, "settings.json");

    public bool RecordOnLaunch
    {
        get
        {
            try
            {
                return !File.Exists(_path)
                    || (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_path)) ?? new()).RecordOnLaunch;
            }
            catch (Exception ex) when (ex is IOException or JsonException) { return true; }
        }
    }

    public async Task SaveAsync(bool recordOnLaunch)
    {
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Preferences(recordOnLaunch)));
        File.Move(temporary, _path, true);
    }
}
