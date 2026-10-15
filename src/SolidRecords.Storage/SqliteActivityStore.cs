using Microsoft.Data.Sqlite;
using SolidRecords.Core;

namespace SolidRecords.Storage;

public sealed class SqliteActivityStore : IActivityStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initialize = new(1, 1);
    private bool _initialized;

    public SqliteActivityStore(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5, Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initialize.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = await OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (version > 1) throw new InvalidOperationException("Databázu vytvorila novšia verzia SolidRecords.");
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS events (
                    id TEXT PRIMARY KEY, recording_id TEXT NOT NULL, kind TEXT NOT NULL,
                    timestamp INTEGER NOT NULL, process_id INTEGER, application TEXT,
                    window_handle INTEGER, title TEXT, url TEXT, reason TEXT);
                CREATE INDEX IF NOT EXISTS ix_events_timestamp ON events(timestamp);
                CREATE TABLE IF NOT EXISTS intervals (
                    id TEXT PRIMARY KEY, recording_id TEXT NOT NULL, process_id INTEGER NOT NULL,
                    application TEXT NOT NULL, window_handle INTEGER NOT NULL, title TEXT NOT NULL,
                    url TEXT, start INTEGER NOT NULL, end INTEGER NOT NULL, is_open INTEGER NOT NULL,
                    CHECK(end >= start));
                CREATE INDEX IF NOT EXISTS ix_intervals_time ON intervals(start, end);
                PRAGMA user_version=1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            // After restart, the heartbeat is the last trustworthy end time.
            command.CommandText = "UPDATE intervals SET is_open=0 WHERE is_open=1;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally { _initialize.Release(); }
    }

    public async Task SaveAsync(RecordingBatch batch, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        foreach (var entry in batch.Events)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO events VALUES ($id,$recording,$kind,$timestamp,$pid,$app,$handle,$title,$url,$reason);
                """;
            Add(command, "$id", entry.Id.ToString());
            Add(command, "$recording", entry.RecordingId.ToString());
            Add(command, "$kind", entry.Kind.ToString());
            Add(command, "$timestamp", entry.Timestamp.ToUnixTimeMilliseconds());
            Add(command, "$pid", entry.ProcessId);
            Add(command, "$app", entry.Application);
            Add(command, "$handle", entry.Window?.Handle);
            Add(command, "$title", entry.Window?.Title);
            Add(command, "$url", entry.Window?.Url);
            Add(command, "$reason", entry.Reason);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var interval in batch.Intervals)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO intervals VALUES ($id,$recording,$pid,$app,$handle,$title,$url,$start,$end,$open)
                ON CONFLICT(id) DO UPDATE SET end=excluded.end, is_open=excluded.is_open;
                """;
            Add(command, "$id", interval.Id.ToString());
            Add(command, "$recording", interval.RecordingId.ToString());
            Add(command, "$pid", interval.Window.ProcessId);
            Add(command, "$app", interval.Window.Application);
            Add(command, "$handle", interval.Window.Handle);
            Add(command, "$title", interval.Window.Title);
            Add(command, "$url", interval.Window.Url);
            Add(command, "$start", interval.Start.ToUnixTimeMilliseconds());
            Add(command, "$end", interval.End.ToUnixTimeMilliseconds());
            Add(command, "$open", interval.IsOpen ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityInterval>> ReadIntervalsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM intervals WHERE start < $to AND end >= $from ORDER BY start;";
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        Add(command, "$to", to.ToUnixTimeMilliseconds());
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ActivityInterval>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7));
            var end = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8));
            result.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                new(reader.GetInt64(4), reader.GetInt32(2), reader.GetString(3), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)),
                start < from ? from : start, end > to ? to : end, reader.GetInt64(9) == 1));
        }
        return result;
    }

    public async Task<IReadOnlyList<ActivityEvent>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM events WHERE timestamp >= $from AND timestamp < $to ORDER BY timestamp, rowid;";
        Add(command, "$from", from.ToUnixTimeMilliseconds());
        Add(command, "$to", to.ToUnixTimeMilliseconds());
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ActivityEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var pid = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
            var app = reader.IsDBNull(5) ? null : reader.GetString(5);
            WindowInfo? window = reader.IsDBNull(6) ? null : new(reader.GetInt64(6), pid ?? 0, app ?? "",
                reader.IsDBNull(7) ? "" : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8));
            result.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                Enum.Parse<EventKind>(reader.GetString(2)), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                window, pid, app, reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        return result;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
