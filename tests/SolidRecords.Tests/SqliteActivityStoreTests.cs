using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SolidRecords.Core;
using SolidRecords.Core.Recording;
using SolidRecords.Core.Reporting;
using SolidRecords.Storage;
using Xunit;

namespace SolidRecords.Tests;

public sealed class SqliteActivityStoreTests : IDisposable
{
    private readonly string _directory = Path.GetFullPath(Path.Combine("artifacts", "storage-tests", Guid.NewGuid().ToString()));
    private SqliteActivityStore CreateStore() => new(Path.Combine(_directory, "activity.db"));

    [Fact]
    public async Task RoundTripsEventsAndIntervalsWithQuotedUnicodeTitles()
    {
        var store = CreateStore();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var window = new WindowInfo(123, 4, "editor", "Žltý 'dokument'; DROP TABLE events;", "https://example.com/?a='b'");
        var recording = Guid.NewGuid();
        var entry = new ActivityEvent(Guid.NewGuid(), recording, EventKind.WindowActivated, at, window, 4, "editor");
        var interval = new ActivityInterval(Guid.NewGuid(), recording, window, at, at.AddMinutes(5), false);
        await store.SaveAsync(Composite(interval) with { Events = [entry] });
        Assert.Equal(entry, Assert.Single(await store.ReadEventsAsync(at.AddSeconds(-1), at.AddHours(1))));
        var actual = Assert.Single(await store.ReadIntervalsAsync(at.AddSeconds(-1), at.AddHours(1)));
        Assert.Equal(window, actual.Window);
        Assert.Equal(TimeSpan.FromMinutes(5), actual.Duration);
    }

    [Fact]
    public async Task UpsertsHeartbeatsAndRecoversOpenIntervalAtLastConfirmedEnd()
    {
        var store = CreateStore();
        var at = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        var interval = new ActivityInterval(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"), at, at, true);
        await store.SaveAsync(Composite(interval));
        await store.SaveAsync(Composite(interval with { End = at.AddSeconds(5) }));
        var recovered = CreateStore();
        await recovered.InitializeAsync();
        var actual = Assert.Single(await recovered.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.False(actual.IsOpen);
        Assert.Equal(TimeSpan.FromSeconds(5), actual.Duration);
        var parent = Assert.Single(await recovered.ReadProcessesAsync(at, at.AddDays(1)));
        Assert.False(parent.IsOpen);
        Assert.False(Assert.Single(parent.Windows).IsOpen);
    }

    [Fact]
    public async Task DayBoundaryClipsIntervalsWithoutDoubleCounting()
    {
        var store = CreateStore();
        var midnight = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(Composite(new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"),
            midnight.AddMinutes(-5), midnight.AddMinutes(10), false)));
        var before = Assert.Single(await store.ReadIntervalsAsync(midnight.AddDays(-1), midnight));
        var after = Assert.Single(await store.ReadIntervalsAsync(midnight, midnight.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(5), before.Duration);
        Assert.Equal(TimeSpan.FromMinutes(10), after.Duration);
    }

    [Fact]
    public async Task BatchIsAtomicWhenAnyEventFails()
    {
        var store = CreateStore();
        var at = DateTimeOffset.UtcNow;
        var entry = new ActivityEvent(Guid.NewGuid(), Guid.NewGuid(), EventKind.RecordingStarted, at);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(new([entry, entry], [])));
        Assert.Empty(await store.ReadEventsAsync(at.AddDays(-1), at.AddDays(1)));
    }

    [Theory]
    [InlineData(2026, 3, 29, 23)]
    [InlineData(2026, 10, 25, 25)]
    public async Task ReportsRespectDaylightSavingTransitions(int year, int month, int day, int hours)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        var date = new DateOnly(year, month, day);
        var start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), timeZone);
        var end = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);
        var store = CreateStore();
        await store.SaveAsync(Composite(new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"), start, end, false)));
        var report = await new DailyReportService(store, timeZone).ReadAsync(date);
        Assert.Equal(TimeSpan.FromHours(hours), report.ActiveDuration);
    }

    [Fact]
    public async Task CompositeReportCountsLeavesOnlyAndPreservesParentsAcrossBackgroundGaps()
    {
        var store = CreateStore();
        var engine = new RecordingEngine();
        var at = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        var window = new WindowInfo(1, 2, "browser", "Tab", "https://example.com");
        foreach (var signal in new CaptureSignal[]
        {
            new(SignalKind.RecordingStarted, at), new(SignalKind.SessionActive, at),
            new(SignalKind.ForegroundChanged, at, window),
            new(SignalKind.ForegroundChanged, at.AddMinutes(2)),
            new(SignalKind.Heartbeat, at.AddMinutes(10)),
            new(SignalKind.ForegroundChanged, at.AddMinutes(20), window),
            new(SignalKind.WindowClosed, at.AddMinutes(23), window)
        }) await store.SaveAsync(engine.Apply(signal));
        var report = await new DailyReportService(store, TimeZoneInfo.Utc).ReadAsync(new(2026, 10, 15));
        var process = Assert.Single(report.Processes);
        var actualWindow = Assert.Single(process.Windows);
        Assert.Equal(2, actualWindow.Activities.Count);
        Assert.Equal(TimeSpan.FromMinutes(23), process.End - process.Start);
        Assert.Equal(TimeSpan.FromMinutes(5), report.ActiveDuration);
        Assert.False(process.IsOpen);
    }

    [Fact]
    public async Task MigratesVersionOneWithoutLosingTitlesUrlsOrTime()
    {
        Directory.CreateDirectory(_directory);
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "activity.db")}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE intervals(id TEXT PRIMARY KEY,recording_id TEXT,process_id INTEGER,
                    application TEXT,window_handle INTEGER,title TEXT,url TEXT,start INTEGER,end INTEGER,is_open INTEGER);
                INSERT INTO intervals VALUES($id,$recording,42,'browser',100,'Old document','https://example.com',1000,61000,1);
                PRAGMA user_version=1;
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$recording", Guid.NewGuid().ToString());
            await command.ExecuteNonQueryAsync();
        }
        var store = CreateStore();
        var process = Assert.Single(await store.ReadProcessesAsync(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)));
        var window = Assert.Single(process.Windows);
        var leaf = Assert.Single(window.Activities);
        Assert.Equal("Old document", leaf.Window.Title);
        Assert.Equal("https://example.com", leaf.Window.Url);
        Assert.Equal(TimeSpan.FromMinutes(1), leaf.Duration);
        Assert.False(leaf.IsOpen);
        // Reinitialization must not migrate the legacy rows a second time.
        Assert.Single(await CreateStore().ReadIntervalsAsync(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)));
    }

    [Fact]
    public async Task LateUrlResolutionUpdatesStoredLeafInsteadOfLeavingAUrlLessDuplicate()
    {
        var store = CreateStore();
        var engine = new RecordingEngine();
        var at = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        var window = new WindowInfo(1, 2, "chrome", "Document");
        foreach (var signal in new CaptureSignal[]
        {
            new(SignalKind.RecordingStarted, at), new(SignalKind.SessionActive, at),
            new(SignalKind.ForegroundChanged, at, window), new(SignalKind.Heartbeat, at.AddSeconds(1)),
            new(SignalKind.ForegroundChanged, at.AddSeconds(2), window with { Url = "https://example.com" }),
            new(SignalKind.ForegroundChanged, at.AddSeconds(5))
        }) await store.SaveAsync(engine.Apply(signal));
        var leaf = Assert.Single(await store.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.Equal("https://example.com", leaf.Window.Url);
        Assert.Equal(at, leaf.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), leaf.Duration);
        Assert.False(leaf.IsOpen);
    }

    [Fact]
    public async Task RetentionPersistsOneMergedLeafAndSeparateActiveSpans()
    {
        var store = CreateStore();
        var engine = new RecordingEngine();
        var policy = new ActivityLogPolicy();
        var at = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var info = new WindowInfo(1, 2, "editor", "Document");
        async Task Send(SignalKind kind, int seconds, WindowInfo? window = null) =>
            await store.SaveAsync(policy.Apply(engine.Apply(new(kind, at.AddSeconds(seconds), window))));
        await Send(SignalKind.RecordingStarted, 0);
        await Send(SignalKind.SessionActive, 0);
        await Send(SignalKind.ForegroundChanged, 0, info);
        await Send(SignalKind.Heartbeat, 14);
        Assert.Empty(await store.ReadIntervalsAsync(at, at.AddDays(1)));
        await Send(SignalKind.ForegroundChanged, 20);
        await Send(SignalKind.ForegroundChanged, 25, info);
        await Send(SignalKind.Heartbeat, 39);
        Assert.False(Assert.Single(await store.ReadIntervalsAsync(at, at.AddDays(1))).IsOpen);
        await Send(SignalKind.Heartbeat, 40);
        var live = Assert.Single(await store.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.True(live.IsOpen);
        Assert.Equal(TimeSpan.FromSeconds(35), live.Duration);
        await Send(SignalKind.RecordingStopped, 45);
        var result = Assert.Single(await store.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.False(result.IsOpen);
        Assert.Equal(TimeSpan.FromSeconds(40), result.Duration);
        Assert.Equal(TimeSpan.FromSeconds(45), result.End - result.Start);
        Assert.Equal(2, result.Spans.Count);
        await using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
        await connection.OpenAsync();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM activity_leaves;";
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task LateUrlMetadataReassignsSpansWithoutOrphans(bool firstUrlKnown, int expectedLeaves)
    {
        var store = CreateStore();
        var policy = new ActivityLogPolicy();
        var at = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var firstBatch = Composite(new(Guid.NewGuid(), Guid.NewGuid(),
            new(1, 2, "browser", "Document", firstUrlKnown ? "https://example.com" : null), at, at.AddSeconds(20), false));
        await store.SaveAsync(policy.Apply(firstBatch));
        var next = firstBatch.Intervals[0] with
        {
            Id = Guid.NewGuid(), Start = at.AddSeconds(25), End = at.AddSeconds(40), IsOpen = true,
            Window = firstBatch.Intervals[0].Window with { Url = null }
        };
        await store.SaveAsync(policy.Apply(new([], [next])));
        await store.SaveAsync(policy.Apply(new([], [next with
        {
            End = at.AddSeconds(41), Window = next.Window with { Url = "https://example.com" }
        }])));
        var leaves = await store.ReadIntervalsAsync(at, at.AddDays(1));
        Assert.Equal(expectedLeaves, leaves.Count);
        Assert.Equal(2, leaves.Sum(leaf => leaf.Spans.Count));
        Assert.Equal(TimeSpan.FromSeconds(36), TimeSpan.FromTicks(leaves.Sum(leaf => leaf.Duration.Ticks)));
        await using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
        await connection.OpenAsync();
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check;";
        using var reader = await check.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task DailyReportFiltersBeforeClippingAtMidnight()
    {
        var store = CreateStore();
        var midnight = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(Composite(new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "editor", "Long document"),
            midnight.AddSeconds(-14), midnight.AddSeconds(1), false)));
        await store.SaveAsync(Composite(new(Guid.NewGuid(), Guid.NewGuid(), new(2, 3, "editor", "Brief document"),
            midnight.AddSeconds(1), midnight.AddSeconds(15), false)));
        var report = await new DailyReportService(store, TimeZoneInfo.Utc).ReadAsync(new(2026, 10, 16));
        Assert.Single(report.Intervals);
        Assert.Equal(TimeSpan.FromSeconds(1), report.ActiveDuration);
    }

    [Fact]
    public async Task VersionTwoMigrationCreatesSpansWithoutChangingActivityTime()
    {
        var store = CreateStore();
        var at = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(Composite(new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "document"),
            at, at.AddSeconds(20), false)));
        await using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE activity_spans; DELETE FROM __EFMigrationsHistory; PRAGMA user_version=2;";
            await command.ExecuteNonQueryAsync();
        }
        var migrated = CreateStore();
        var interval = Assert.Single(await migrated.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.Single(interval.Spans);
        Assert.Equal(TimeSpan.FromSeconds(20), interval.Duration);
    }

    [Fact]
    public async Task VersionThreeMigrationPreservesHistoryAndAddsPersistentIcons()
    {
        var store = CreateStore();
        var at = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var leaf = new ActivityInterval(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "editor", "Document"),
            at, at.AddSeconds(20), false);
        await store.SaveAsync(Composite(leaf));
        await using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE application_icons; DELETE FROM __EFMigrationsHistory; PRAGMA user_version=3;";
            await command.ExecuteNonQueryAsync();
        }
        var migrated = CreateStore();
        var before = Assert.Single(await migrated.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.Null(before.ApplicationIconPngBase64);
        Assert.Equal(leaf.Id, before.Id);
        Assert.Single(before.Spans);
        var png = Convert.ToBase64String([137, 80, 78, 71]);
        await migrated.SaveAsync(new([], []) { ApplicationIcons = [new("EDITOR", png)] });
        var after = Assert.Single(await CreateStore().ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.Equal(png, after.ApplicationIconPngBase64);
        Assert.Equal(before.Window, after.Window);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Duration, after.Duration);
        Assert.Equal(before.Spans, after.Spans);
        // No icon in a later batch must not erase the collected icon.
        await migrated.SaveAsync(new([], []));
        Assert.Equal(png, Assert.Single((await new DailyReportService(migrated, TimeZoneInfo.Utc)
            .ReadAsync(new(2026, 10, 16))).Intervals).ApplicationIconPngBase64);
        await using var check = new SqliteConnection($"Data Source={store.DatabasePath}");
        await check.OpenAsync();
        using var version = check.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(4L, await version.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RecorderPersistsIconBeforeActivityMeetsMinimumAndKeepsItAfterRestart()
    {
        var store = CreateStore();
        var source = new IconSource();
        var at = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var png = Convert.ToBase64String([137, 80, 78, 71]);
        await using (var recorder = new Recorder(store, source, new IconClock(at)))
        {
            await recorder.StartAsync();
            source.Publish!(new(SignalKind.SessionActive, at));
            source.Publish!(new(SignalKind.ForegroundChanged, at, new(1, 2, "editor", "Document"),
                Icon: new("editor", png)));
            source.Publish!(new(SignalKind.ForegroundChanged, at.AddSeconds(20)));
            await recorder.StopAsync();
        }
        var interval = Assert.Single(await CreateStore().ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.Equal(png, interval.ApplicationIconPngBase64);
        Assert.Equal(TimeSpan.FromSeconds(20), interval.Duration);
    }

    [Fact]
    public async Task FreshDatabaseAppliesEfMigrationsAndRepeatedInitializationKeepsLiveActivityOpen()
    {
        var store = CreateStore();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.InitializeAsync()));
        var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={store.DatabasePath}").Options;
        await using var db = new ActivityDbContext(options);
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var at = DateTimeOffset.UnixEpoch;
        await store.SaveAsync(Composite(new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "live"),
            at, at.AddSeconds(20), true)));
        await store.InitializeAsync();
        Assert.True(Assert.Single(await store.ReadIntervalsAsync(at, at.AddDays(1))).IsOpen);
    }

    [Fact]
    public async Task AdoptsVersionFourWithoutChangingIdsSpansIconsOrEventOrder()
    {
        var store = CreateStore();
        var at = DateTimeOffset.UnixEpoch;
        var id = Guid.NewGuid();
        var recording = Guid.NewGuid();
        var spans = new ActivitySpan[] { new(Guid.NewGuid(), at, at.AddSeconds(20)),
            new(Guid.NewGuid(), at.AddSeconds(25), at.AddSeconds(45)) };
        var png = Convert.ToBase64String([137, 80, 78, 71]);
        var first = new ActivityEvent(Guid.NewGuid(), recording, EventKind.RecordingStarted, at);
        var second = new ActivityEvent(Guid.NewGuid(), recording, EventKind.Heartbeat, at);
        await store.SaveAsync(Composite(new(id, recording, new(1, 2, "editor", "Document"), at, at.AddSeconds(45), false)
        { Spans = spans }) with { Events = [first, second], ApplicationIcons = [new("EDITOR", png)] });
        // A pre-EF database has the same tables, but no EF migration history.
        await using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE __EFMigrationsHistory;";
            await command.ExecuteNonQueryAsync();
        }
        var adopted = CreateStore();
        await adopted.InitializeAsync();
        var leaf = Assert.Single(await adopted.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.Equal(id, leaf.Id);
        Assert.Equal(spans, leaf.Spans);
        Assert.Equal(png, leaf.ApplicationIconPngBase64);
        Assert.Equal(new[] { first, second }, await adopted.ReadEventsAsync(at, at.AddDays(1)));
        await adopted.SaveAsync(new([], []) { ApplicationIcons = [new("editor", Convert.ToBase64String([1, 2, 3]))] });
        Assert.Equal(Convert.ToBase64String([1, 2, 3]),
            Assert.Single(await adopted.ReadIntervalsAsync(at, at.AddDays(1))).ApplicationIconPngBase64);
        Assert.Equal(spans, Assert.Single(await CreateStore().ReadIntervalsAsync(at, at.AddDays(1))).Spans);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsUnknownSchemaBeforeRecoveryOrSchemaChanges(bool unknownEfMigration)
    {
        var store = CreateStore();
        var at = DateTimeOffset.UnixEpoch;
        await store.SaveAsync(Composite(new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "live"),
            at, at.AddSeconds(20), true)));
        await using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = unknownEfMigration
            ? "INSERT INTO __EFMigrationsHistory VALUES('20990101000000_Future','10.0.12');"
            : "PRAGMA user_version=5;";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateStore().InitializeAsync());
        command.CommandText = "SELECT is_open FROM activity_leaves;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(unknownEfMigration ? 4L : 5L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task FailedBatchRollsBackParentsLeavesSpansAndIcons()
    {
        var store = CreateStore();
        var at = DateTimeOffset.UnixEpoch;
        var leaf = new ActivityInterval(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"),
            at, at.AddSeconds(20), false);
        var entry = new ActivityEvent(Guid.NewGuid(), leaf.RecordingId, EventKind.RecordingStarted, at);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(Composite(leaf) with
        { Events = [entry, entry], ApplicationIcons = [new("app", Convert.ToBase64String([1, 2, 3]))] }));
        Assert.Empty(await store.ReadProcessesAsync(at, at.AddDays(1)));
        Assert.Empty(await store.ReadEventsAsync(at, at.AddDays(1)));
        await using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM window_activities) + (SELECT COUNT(*) FROM activity_leaves) + (SELECT COUNT(*) FROM activity_spans) + (SELECT COUNT(*) FROM application_icons);";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task InterruptedLegacyConversionCanBeRetriedWithoutDuplicatingHistory()
    {
        Directory.CreateDirectory(_directory);
        var store = CreateStore();
        var id = Guid.NewGuid();
        var recording = Guid.NewGuid();
        await using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE intervals(id TEXT PRIMARY KEY,recording_id TEXT,process_id INTEGER,
                application TEXT,window_handle INTEGER,title TEXT,url TEXT,start INTEGER,end INTEGER,is_open INTEGER);
            INSERT INTO intervals VALUES($id,$recording,42,'editor',100,'Document',NULL,1000,0,1);
            PRAGMA user_version=1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$recording", recording.ToString());
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => store.InitializeAsync());
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM process_activities;";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "UPDATE intervals SET end=61000;";
        await command.ExecuteNonQueryAsync();
        await store.InitializeAsync();
        var result = Assert.Single(await store.ReadIntervalsAsync(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)));
        Assert.Equal(id, result.Id);
        Assert.Equal(TimeSpan.FromMinutes(1), result.Duration);
        Assert.Single(result.Spans);
        Assert.Single(await CreateStore().ReadProcessesAsync(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)));
        command.CommandText = "SELECT COUNT(*) FROM intervals;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    private sealed class IconClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private sealed class IconSource : IActivitySource
    {
        public Action<CaptureSignal>? Publish { get; private set; }
        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default)
        {
            Publish = publish;
            return Task.CompletedTask;
        }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static RecordingBatch Composite(ActivityInterval leaf)
    {
        var process = new ProcessActivity(leaf.Id, leaf.RecordingId, leaf.Window.ProcessId,
            leaf.Window.Application, leaf.Window.ProcessStartTicks, leaf.Start, leaf.End, leaf.IsOpen);
        var window = new WindowActivity(leaf.Id, process.Id, leaf.Window.Handle, leaf.Window.Title,
            leaf.Start, leaf.End, leaf.IsOpen);
        return new([], [leaf with { ProcessActivityId = process.Id, WindowActivityId = window.Id }])
        { Processes = [process], Windows = [window] };
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
