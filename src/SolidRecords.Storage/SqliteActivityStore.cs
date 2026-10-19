using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SolidRecords.Core;
using SolidRecords.Core.Recording;

namespace SolidRecords.Storage;

public sealed class SqliteActivityStore : IActivityStore, ISettingsStore
{
    private readonly DbContextOptions<ActivityDbContext> _options;
    private readonly SemaphoreSlim _initialize = new(1, 1);
    private bool _initialized;

    public SqliteActivityStore(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5, Pooling = true, ForeignKeys = true
        }.ToString()).Options;
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initialize.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var db = new ActivityDbContext(_options);
            await SqliteSchema.InitializeAsync(db, cancellationToken);
            _initialized = true;
        }
        finally { _initialize.Release(); }
    }

    public async Task<ApplicationSettings?> ReadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = new ActivityDbContext(_options);
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (row is null) return null;
        var settings = System.Text.Json.JsonSerializer.Deserialize<ApplicationSettings>(row.Value)
            ?? throw new InvalidOperationException("Nastavenia v databáze nie sú platné.");
        settings.Validate();
        return settings;
    }

    public async Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        await InitializeAsync(cancellationToken);
        await using var db = new ActivityDbContext(_options);
        var row = await db.Settings.FindAsync([1], cancellationToken);
        if (row is null) { row = new() { Id = 1 }; db.Settings.Add(row); }
        row.Value = System.Text.Json.JsonSerializer.Serialize(settings);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(RecordingBatch batch, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = new ActivityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WriteBatchAsync(db, batch, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = new ActivityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = Millis(cutoff);
        // Retain complete crossing/open activities, including their original spans and IDs.
        await db.Leaves.Where(x => !x.IsOpen && x.End < before).ExecuteDeleteAsync(cancellationToken);
        await db.Windows.Where(x => !x.IsOpen && x.End < before &&
            !db.Leaves.Any(leaf => leaf.WindowActivityId == x.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.Processes.Where(x => !x.IsOpen && x.End < before &&
            !db.Windows.Any(window => window.ProcessActivityId == x.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.Events.Where(x => x.Timestamp < before).ExecuteDeleteAsync(cancellationToken);
        // The legacy table is unmapped and may be absent in a fresh database.
        var hasLegacyIntervals = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'intervals'")
            .SingleAsync(cancellationToken);
        if (hasLegacyIntervals != 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM intervals WHERE end < {before}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task WriteBatchAsync(ActivityDbContext db, RecordingBatch batch, CancellationToken cancellationToken)
    {
        foreach (var icon in batch.ApplicationIcons)
        {
            // The database's NOCASE collation is authoritative, including for existing casing.
            var row = await db.Icons.FirstOrDefaultAsync(x => x.Application == icon.Application, cancellationToken);
            if (row is null) db.Icons.Add(new() { Application = icon.Application, Png = Convert.FromBase64String(icon.PngBase64) });
            else row.Png = Convert.FromBase64String(icon.PngBase64);
            await db.SaveChangesAsync(cancellationToken);
        }
        foreach (var process in batch.Processes)
        {
            var row = await db.Processes.FindAsync([process.Id.ToString()], cancellationToken);
            if (row is null)
            {
                row = new() { Id = process.Id.ToString(), RecordingId = process.RecordingId.ToString(),
                    ProcessId = process.ProcessId, Application = process.Application, ProcessStartTicks = process.ProcessStartTicks,
                    Start = Millis(process.Start) };
                db.Processes.Add(row);
            }
            row.End = Millis(process.End);
            row.IsOpen = process.IsOpen;
        }
        foreach (var window in batch.Windows)
        {
            var row = await db.Windows.FindAsync([window.Id.ToString()], cancellationToken);
            if (row is null)
            {
                row = new() { Id = window.Id.ToString(), ProcessActivityId = window.ProcessActivityId.ToString(),
                    Handle = window.Handle, Start = Millis(window.Start) };
                db.Windows.Add(row);
            }
            row.Title = window.Title;
            row.End = Millis(window.End);
            row.IsOpen = window.IsOpen;
        }
        foreach (var interval in batch.Intervals)
        {
            var row = await db.Leaves.FindAsync([interval.Id.ToString()], cancellationToken);
            if (row is null)
            {
                row = new() { Id = interval.Id.ToString(), WindowActivityId = interval.WindowActivityId.ToString(),
                    Title = interval.Window.Title, Start = Millis(interval.Start) };
                db.Leaves.Add(row);
            }
            row.Url = interval.Window.Url;
            row.End = Millis(interval.End);
            row.IsOpen = interval.IsOpen;
        }
        await db.SaveChangesAsync(cancellationToken);
        // Persist reassigned spans before deleting leaves with cascading children.
        foreach (var interval in batch.Intervals)
        foreach (var span in ActivityLogPolicy.GetSpans(interval))
        {
            var row = await db.Spans.FindAsync([span.Id.ToString()], cancellationToken);
            if (row is null)
            {
                row = new() { Id = span.Id.ToString() };
                db.Spans.Add(row);
            }
            row.ActivityId = interval.Id.ToString();
            row.Start = Millis(span.Start);
            row.End = Millis(span.End);
        }
        await db.SaveChangesAsync(cancellationToken);
        var removed = batch.RemovedIntervalIds.Select(x => x.ToString()).ToArray();
        if (removed.Length > 0)
            await db.Leaves.Where(x => removed.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in batch.Events)
        {
            db.Events.Add(new() { Id = entry.Id.ToString(), RecordingId = entry.RecordingId.ToString(),
                Kind = entry.Kind.ToString(), Timestamp = Millis(entry.Timestamp), ProcessId = entry.ProcessId,
                Application = entry.Application, WindowHandle = entry.Window?.Handle, Title = entry.Window?.Title,
                Url = entry.Window?.Url, Reason = entry.Reason });
            // Keep insertion order for events with the same timestamp, and let the database reject duplicates.
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<ActivitySpan>> ReadActivitySpansAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = new ActivityDbContext(_options);
        // Navigation needs only actual foreground spans, without parent lifetimes, titles or icons.
        var spans = await db.Spans.AsNoTracking().Select(x => new { x.Id, x.Start, x.End })
            .ToArrayAsync(cancellationToken);
        return spans.Select(x => new ActivitySpan(Guid.Parse(x.Id), Time(x.Start), Time(x.End))).ToArray();
    }

    public async Task<IReadOnlyList<ProcessActivity>> ReadProcessesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = new ActivityDbContext(_options);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        // All component queries see the same committed snapshot without reserving the writer lock.
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        var start = Millis(from);
        var end = Millis(to);
        var processQuery = db.Processes.Where(x => x.Start < end && x.End >= start);
        var windowQuery = db.Windows.Where(x => x.Start < end && x.End >= start);
        var leafQuery = db.Leaves.Where(x => x.Start < end && x.End >= start);
        var processes = await processQuery.OrderBy(x => x.Start).ToArrayAsync(cancellationToken);
        var windows = await windowQuery.OrderBy(x => x.Start).ToArrayAsync(cancellationToken);
        var leaves = await (from leaf in leafQuery
                            join window in windowQuery on leaf.WindowActivityId equals window.Id
                            join process in processQuery on window.ProcessActivityId equals process.Id
                            join icon in db.Icons on EF.Functions.Collate(process.Application, "NOCASE") equals icon.Application into icons
                            from icon in icons.DefaultIfEmpty()
                            orderby leaf.Start
                            select new { Leaf = leaf, Process = process, Window = window, Png = icon == null ? null : icon.Png })
            .ToArrayAsync(cancellationToken);
        var spans = await (from span in db.Spans
                           join leaf in leafQuery on span.ActivityId equals leaf.Id
                           orderby span.Start
                           select span).ToArrayAsync(cancellationToken);
        var spansByActivity = spans.ToLookup(x => x.ActivityId,
            x => new ActivitySpan(Guid.Parse(x.Id), Time(x.Start), Time(x.End)));
        var leavesByWindow = leaves.Select(x => new ActivityInterval(Guid.Parse(x.Leaf.Id), Guid.Parse(x.Process.RecordingId),
            new(x.Window.Handle, x.Process.ProcessId, x.Process.Application, x.Leaf.Title, x.Leaf.Url, x.Process.ProcessStartTicks),
            Time(x.Leaf.Start), Time(x.Leaf.End), x.Leaf.IsOpen, Guid.Parse(x.Process.Id), Guid.Parse(x.Window.Id))
        {
            Spans = spansByActivity[x.Leaf.Id].ToArray(),
            ApplicationIconPngBase64 = x.Png is null ? null : Convert.ToBase64String(x.Png)
        }).ToLookup(x => x.WindowActivityId);
        var windowsByProcess = windows.Select(x => new WindowActivity(Guid.Parse(x.Id), Guid.Parse(x.ProcessActivityId),
            x.Handle, x.Title, Time(x.Start), Time(x.End), x.IsOpen)
        { Activities = leavesByWindow[Guid.Parse(x.Id)].ToArray() }).ToLookup(x => x.ProcessActivityId);
        return processes.Select(x => new ProcessActivity(Guid.Parse(x.Id), Guid.Parse(x.RecordingId), x.ProcessId,
            x.Application, x.ProcessStartTicks, Time(x.Start), Time(x.End), x.IsOpen)
        { Windows = windowsByProcess[Guid.Parse(x.Id)].ToArray() }).ToArray();
    }

    public async Task<IReadOnlyList<ActivityInterval>> ReadIntervalsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default) => (await ReadProcessesAsync(from, to, cancellationToken))
        .SelectMany(p => p.Windows).SelectMany(w => w.Activities)
        .Select(interval => ActivityLogPolicy.Clip(interval, from, to)).OfType<ActivityInterval>().OrderBy(x => x.Start).ToArray();

    public async Task<IReadOnlyList<ActivityEvent>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = new ActivityDbContext(_options);
        // rowid preserves historical insertion order when timestamps are equal.
        var rows = await db.Events.FromSqlInterpolated($"SELECT * FROM events WHERE timestamp >= {Millis(from)} AND timestamp < {Millis(to)} ORDER BY timestamp,rowid")
            .AsNoTracking().ToArrayAsync(cancellationToken);
        return rows.Select(x => new ActivityEvent(Guid.Parse(x.Id), Guid.Parse(x.RecordingId), Enum.Parse<EventKind>(x.Kind),
            Time(x.Timestamp), x.WindowHandle is long handle ? new WindowInfo(handle, x.ProcessId ?? 0, x.Application ?? "",
                x.Title ?? "", x.Url) : null, x.ProcessId, x.Application, x.Reason)).ToArray();
    }

    private static long Millis(DateTimeOffset time) => time.ToUnixTimeMilliseconds();
    private static DateTimeOffset Time(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
