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
            DefaultTimeout = 5, Pooling = true, ForeignKeys = true
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
            await SqliteSchema.InitializeAsync(connection, cancellationToken);
            _initialized = true;
        }
        finally { _initialize.Release(); }
    }

    public async Task SaveAsync(RecordingBatch batch, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await WriteBatchAsync(connection, transaction, batch, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task WriteBatchAsync(SqliteConnection connection, SqliteTransaction transaction,
        RecordingBatch batch, CancellationToken cancellationToken)
    {
        async Task Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters) Add(command, name, value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var process in batch.Processes)
            await Execute("""
                INSERT INTO process_activities(id,recording_id,process_id,application,process_start_ticks,start,end,is_open)
                VALUES($id,$recording,$pid,$app,$ticks,$start,$end,$open)
                ON CONFLICT(id) DO UPDATE SET end=excluded.end, is_open=excluded.is_open;
                """, ("$id", process.Id.ToString()), ("$recording", process.RecordingId.ToString()),
                ("$pid", process.ProcessId), ("$app", process.Application), ("$ticks", process.ProcessStartTicks),
                ("$start", Millis(process.Start)), ("$end", Millis(process.End)), ("$open", process.IsOpen));

        foreach (var window in batch.Windows)
            await Execute("""
                INSERT INTO window_activities(id,process_activity_id,handle,title,start,end,is_open)
                VALUES($id,$parent,$handle,$title,$start,$end,$open)
                ON CONFLICT(id) DO UPDATE SET title=excluded.title,end=excluded.end,is_open=excluded.is_open;
                """, ("$id", window.Id.ToString()), ("$parent", window.ProcessActivityId.ToString()),
                ("$handle", window.Handle), ("$title", window.Title), ("$start", Millis(window.Start)),
                ("$end", Millis(window.End)), ("$open", window.IsOpen));

        foreach (var interval in batch.Intervals)
            await Execute("""
                INSERT INTO activity_leaves(id,window_activity_id,title,url,start,end,is_open)
                VALUES($id,$parent,$title,$url,$start,$end,$open)
                ON CONFLICT(id) DO UPDATE SET end=excluded.end,is_open=excluded.is_open;
                """, ("$id", interval.Id.ToString()), ("$parent", interval.WindowActivityId.ToString()),
                ("$title", interval.Window.Title), ("$url", interval.Window.Url), ("$start", Millis(interval.Start)),
                ("$end", Millis(interval.End)), ("$open", interval.IsOpen));

        foreach (var entry in batch.Events)
            await Execute("""
                INSERT INTO events(id,recording_id,kind,timestamp,process_id,application,window_handle,title,url,reason)
                VALUES($id,$recording,$kind,$timestamp,$pid,$app,$handle,$title,$url,$reason);
                """, ("$id", entry.Id.ToString()), ("$recording", entry.RecordingId.ToString()),
                ("$kind", entry.Kind.ToString()), ("$timestamp", Millis(entry.Timestamp)), ("$pid", entry.ProcessId),
                ("$app", entry.Application), ("$handle", entry.Window?.Handle), ("$title", entry.Window?.Title),
                ("$url", entry.Window?.Url), ("$reason", entry.Reason));
    }

    public async Task<IReadOnlyList<ProcessActivity>> ReadProcessesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        // One read transaction prevents a heartbeat between queries producing inconsistent composites.
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        Add(command, "$from", Millis(from));
        Add(command, "$to", Millis(to));
        command.CommandText = "SELECT * FROM process_activities WHERE start < $to AND end >= $from ORDER BY start;";
        var processes = new Dictionary<Guid, ProcessActivity>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var process = new ProcessActivity(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    Time(reader.GetInt64(5)), Time(reader.GetInt64(6)), reader.GetBoolean(7));
                processes.Add(process.Id, process);
            }
        command.CommandText = "SELECT * FROM window_activities WHERE start < $to AND end >= $from ORDER BY start;";
        var windows = new Dictionary<Guid, WindowActivity>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var parent = Guid.Parse(reader.GetString(1));
                if (!processes.ContainsKey(parent)) continue;
                var window = new WindowActivity(Guid.Parse(reader.GetString(0)), parent, reader.GetInt64(2), reader.GetString(3),
                    Time(reader.GetInt64(4)), Time(reader.GetInt64(5)), reader.GetBoolean(6));
                windows.Add(window.Id, window);
            }
        command.CommandText = "SELECT * FROM activity_leaves WHERE start < $to AND end >= $from ORDER BY start;";
        var activities = new List<ActivityInterval>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var parent = Guid.Parse(reader.GetString(1));
                if (!windows.TryGetValue(parent, out var window)) continue;
                var process = processes[window.ProcessActivityId];
                var start = Time(reader.GetInt64(4));
                var end = Time(reader.GetInt64(5));
                activities.Add(new(Guid.Parse(reader.GetString(0)), process.RecordingId,
                    new(window.Handle, process.ProcessId, process.Application, reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3), process.ProcessStartTicks),
                    start < from ? from : start, end > to ? to : end, reader.GetBoolean(6), process.Id, window.Id));
            }
        var leavesByWindow = activities.ToLookup(x => x.WindowActivityId);
        var windowsByProcess = windows.Values.Select(w => w with { Activities = leavesByWindow[w.Id].ToArray() })
            .ToLookup(w => w.ProcessActivityId);
        return processes.Values.Select(p => p with { Windows = windowsByProcess[p.Id].ToArray() }).ToArray();
    }

    public async Task<IReadOnlyList<ActivityInterval>> ReadIntervalsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default) => (await ReadProcessesAsync(from, to, cancellationToken))
        .SelectMany(p => p.Windows).SelectMany(w => w.Activities).OrderBy(x => x.Start).ToArray();

    public async Task<IReadOnlyList<ActivityEvent>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM events WHERE timestamp >= $from AND timestamp < $to ORDER BY timestamp,rowid;";
        Add(command, "$from", Millis(from));
        Add(command, "$to", Millis(to));
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ActivityEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var pid = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
            var app = reader.IsDBNull(5) ? null : reader.GetString(5);
            WindowInfo? window = reader.IsDBNull(6) ? null : new(reader.GetInt64(6), pid ?? 0, app ?? "",
                reader.IsDBNull(7) ? "" : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8));
            result.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                Enum.Parse<EventKind>(reader.GetString(2)), Time(reader.GetInt64(3)), window, pid, app,
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        return result;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static long Millis(DateTimeOffset time) => time.ToUnixTimeMilliseconds();
    private static DateTimeOffset Time(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
