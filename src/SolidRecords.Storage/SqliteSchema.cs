using Microsoft.Data.Sqlite;
using SolidRecords.Core;

namespace SolidRecords.Storage;

internal static class SqliteSchema
{
    public static async Task InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (version > 2) throw new InvalidOperationException("Databázu vytvorila novšia verzia SolidRecords.");
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY, recording_id TEXT NOT NULL, kind TEXT NOT NULL,
                timestamp INTEGER NOT NULL, process_id INTEGER, application TEXT,
                window_handle INTEGER, title TEXT, url TEXT, reason TEXT);
            CREATE INDEX IF NOT EXISTS ix_events_timestamp ON events(timestamp);
            CREATE TABLE IF NOT EXISTS process_activities (
                id TEXT PRIMARY KEY, recording_id TEXT NOT NULL, process_id INTEGER NOT NULL,
                application TEXT NOT NULL, process_start_ticks INTEGER, start INTEGER NOT NULL,
                end INTEGER NOT NULL, is_open INTEGER NOT NULL, CHECK(end >= start));
            CREATE TABLE IF NOT EXISTS window_activities (
                id TEXT PRIMARY KEY, process_activity_id TEXT NOT NULL REFERENCES process_activities(id),
                handle INTEGER NOT NULL, title TEXT NOT NULL, start INTEGER NOT NULL,
                end INTEGER NOT NULL, is_open INTEGER NOT NULL, CHECK(end >= start));
            CREATE TABLE IF NOT EXISTS activity_leaves (
                id TEXT PRIMARY KEY, window_activity_id TEXT NOT NULL REFERENCES window_activities(id),
                title TEXT NOT NULL, url TEXT, start INTEGER NOT NULL,
                end INTEGER NOT NULL, is_open INTEGER NOT NULL, CHECK(end >= start));
            CREATE INDEX IF NOT EXISTS ix_process_time ON process_activities(start,end);
            CREATE INDEX IF NOT EXISTS ix_window_time ON window_activities(start,end);
            CREATE INDEX IF NOT EXISTS ix_window_parent ON window_activities(process_activity_id);
            CREATE INDEX IF NOT EXISTS ix_leaf_time ON activity_leaves(start,end);
            CREATE INDEX IF NOT EXISTS ix_leaf_parent ON activity_leaves(window_activity_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (version == 1) await MigrateLegacyAsync(connection, transaction, cancellationToken);
        command.CommandText = """
            UPDATE activity_leaves SET is_open=0 WHERE is_open=1;
            UPDATE window_activities SET is_open=0 WHERE is_open=1;
            UPDATE process_activities SET is_open=0 WHERE is_open=1;
            PRAGMA user_version=2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task MigrateLegacyAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM intervals ORDER BY start;";
        var legacy = new List<ActivityInterval>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                legacy.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                    new(reader.GetInt64(4), reader.GetInt32(2), reader.GetString(3), reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6)),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8)), false));
        var processes = new List<ProcessActivity>();
        var windows = new List<WindowActivity>();
        var leaves = new List<ActivityInterval>();
        foreach (var processGroup in legacy.GroupBy(x => (x.RecordingId, x.Window.ProcessId, x.Window.Application)))
        {
            var process = new ProcessActivity(Guid.NewGuid(), processGroup.Key.RecordingId,
                processGroup.Key.ProcessId, processGroup.Key.Application, null,
                processGroup.Min(x => x.Start), processGroup.Max(x => x.End), false);
            processes.Add(process);
            foreach (var windowGroup in processGroup.GroupBy(x => x.Window.Handle))
            {
                var window = new WindowActivity(Guid.NewGuid(), process.Id, windowGroup.Key,
                    windowGroup.Last().Window.Title, windowGroup.Min(x => x.Start), windowGroup.Max(x => x.End), false);
                windows.Add(window);
                leaves.AddRange(windowGroup.Select(x => x with { ProcessActivityId = process.Id, WindowActivityId = window.Id }));
            }
        }
        await SqliteActivityStore.WriteBatchAsync(connection, transaction,
            new([], leaves) { Processes = processes, Windows = windows }, cancellationToken);
        // The original intervals table is preserved; migration is transactional and never deletes history.
    }
}
