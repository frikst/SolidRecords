using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SolidRecords.Core;

namespace SolidRecords.Storage;

internal static class SqliteSchema
{
    public static async Task InitializeAsync(ActivityDbContext db, CancellationToken cancellationToken)
    {
        // Keep this connection open: synchronous and other PRAGMAs are connection scoped.
        await db.Database.OpenConnectionAsync(cancellationToken);
        var versions = await db.Database.SqlQueryRaw<long>("PRAGMA user_version;").ToArrayAsync(cancellationToken);
        var version = versions.Single();
        if (version > 4) throw new InvalidOperationException("Databázu vytvorila novšia verzia SolidRecords.");
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (applied.Any(x => !known.Contains(x)))
            throw new InvalidOperationException("Databázu vytvorila novšia verzia SolidRecords.");
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;", cancellationToken);
        if (version < 4)
        {
            // Finish legacy conversion against the original layout before applying future EF migrations.
            // An interrupted conversion can be retried: schema adoption and data conversion are separate commits.
            if (applied.Length == 0)
                await db.GetService<IMigrator>().MigrateAsync(known.Order(StringComparer.Ordinal).First(), cancellationToken);
            await using var upgrade = await db.Database.BeginTransactionAsync(cancellationToken);
            if (version == 1) await MigrateLegacyAsync(db, cancellationToken);
            if (version < 3)
                await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO activity_spans(id,activity_id,start,end) SELECT id,id,start,end FROM activity_leaves;", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA user_version=4;", cancellationToken);
            await upgrade.CommitAsync(cancellationToken);
        }
        await db.Database.MigrateAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Leaves.Where(x => x.IsOpen).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsOpen, false), cancellationToken);
        await db.Windows.Where(x => x.IsOpen).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsOpen, false), cancellationToken);
        await db.Processes.Where(x => x.IsOpen).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsOpen, false), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task MigrateLegacyAsync(ActivityDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<LegacyIntervalRow>("""
            SELECT id AS Id, recording_id AS RecordingId, process_id AS ProcessId, application AS Application,
                window_handle AS WindowHandle, title AS Title, url AS Url, start AS Start, end AS End
            FROM intervals ORDER BY start
            """).ToArrayAsync(cancellationToken);
        var legacy = rows.Select(x => new ActivityInterval(Guid.Parse(x.Id), Guid.Parse(x.RecordingId),
            new(x.WindowHandle, x.ProcessId, x.Application, x.Title, x.Url),
            DateTimeOffset.FromUnixTimeMilliseconds(x.Start), DateTimeOffset.FromUnixTimeMilliseconds(x.End), false));
        // Use the original column layout here: later EF migrations have not run yet.
        foreach (var processGroup in legacy.GroupBy(x => (x.RecordingId, x.Window.ProcessId, x.Window.Application)))
        {
            var processId = Guid.NewGuid().ToString();
            var recordingId = processGroup.Key.RecordingId.ToString();
            var pid = processGroup.Key.ProcessId;
            var application = processGroup.Key.Application;
            var start = processGroup.Min(x => x.Start).ToUnixTimeMilliseconds();
            var end = processGroup.Max(x => x.End).ToUnixTimeMilliseconds();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO process_activities(id,recording_id,process_id,application,process_start_ticks,start,end,is_open)
                VALUES({processId},{recordingId},{pid},{application},NULL,{start},{end},0)
                """, cancellationToken);
            foreach (var windowGroup in processGroup.GroupBy(x => x.Window.Handle))
            {
                var windowId = Guid.NewGuid().ToString();
                var handle = windowGroup.Key;
                var title = windowGroup.Last().Window.Title;
                start = windowGroup.Min(x => x.Start).ToUnixTimeMilliseconds();
                end = windowGroup.Max(x => x.End).ToUnixTimeMilliseconds();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO window_activities(id,process_activity_id,handle,title,start,end,is_open)
                    VALUES({windowId},{processId},{handle},{title},{start},{end},0)
                    """, cancellationToken);
                foreach (var leaf in windowGroup)
                {
                    var leafId = leaf.Id.ToString();
                    title = leaf.Window.Title;
                    var url = leaf.Window.Url;
                    start = leaf.Start.ToUnixTimeMilliseconds();
                    end = leaf.End.ToUnixTimeMilliseconds();
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO activity_leaves(id,window_activity_id,title,url,start,end,is_open)
                        VALUES({leafId},{windowId},{title},{url},{start},{end},0)
                        """, cancellationToken);
                }
            }
        }
        // The original intervals table is preserved; migration is transactional and never deletes history.
    }
}

internal sealed class LegacyIntervalRow
{
    public string Id { get; set; } = "";
    public string RecordingId { get; set; } = "";
    public int ProcessId { get; set; }
    public string Application { get; set; } = "";
    public long WindowHandle { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
}
