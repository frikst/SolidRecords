using System.Text.Json;
using SolidRecords.Core.Updates;

namespace SolidRecords.App.Updates;

internal sealed class UpdateStateStore(string directory) : IUpdateStateStore
{
    private readonly string _path = Path.Combine(directory, "updates.json");

    public async Task<UpdateState> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new();
        // Fail closed on corrupt state: the background service retries locally, without flooding the API.
        return JsonSerializer.Deserialize<UpdateState>(await File.ReadAllTextAsync(_path, cancellationToken))
            ?? throw new JsonException("Neplatný stav kontroly aktualizácií.");
    }

    public async Task SaveAsync(UpdateState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state), cancellationToken);
        File.Move(temporary, _path, overwrite: true);
    }
}
