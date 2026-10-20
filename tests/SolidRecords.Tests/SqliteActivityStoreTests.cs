using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstForegroundAtStartupOrMorningUnlockReachesReportWithoutSwitchingWindows(bool startsLocked)
    {
        var store = CreateStore();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        var at = new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
        var started = startsLocked ? at.AddHours(-10) : at;
        var window = new WindowInfo(100, 42, "editor", "First document");
        var source = new IconSource
        {
            StartupSignals = startsLocked
                ? [new(SignalKind.SessionInactive, started, Reason: "RecordingBaseline")]
                : [new(SignalKind.SessionActive, at, Reason: "RecordingBaseline"), new(SignalKind.ForegroundChanged, at, window)]
        };
        var options = new ActivityTrackingOptions();
        await using var recorder = new Recorder(store, source, new IconClock(started), options);
        var reports = new DailyReportService(store, zone, options);
        await recorder.StartAsync();
        if (startsLocked)
        {
            source.Publish!(new(SignalKind.SessionActive, at));
            source.Publish(new(SignalKind.ForegroundChanged, at, window));
        }
        source.Publish!(new(SignalKind.Heartbeat, at.AddSeconds(14)));
        // A retention request is also an awaited queue barrier; this cutoff deletes nothing.
        await recorder.DeleteBeforeAsync(DateTimeOffset.MinValue);
        Assert.Empty((await reports.ReadAsync(new(2026, 10, 19))).Intervals);

        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(15)));
        await recorder.DeleteBeforeAsync(DateTimeOffset.MinValue);
        var report = await reports.ReadAsync(new(2026, 10, 19));
        var first = Assert.Single(report.Intervals);
        Assert.Equal(at, first.Start);
        Assert.Equal(at, report.FirstActivity);
        Assert.Equal(TimeSpan.FromSeconds(15), report.ActiveDuration);
        Assert.True(first.IsOpen);
        Assert.Equal(window, first.Window);
        Assert.Equal(new DateOnly[] { new(2026, 10, 19) }, await reports.ReadActivityDatesAsync());

        source.Publish(new(SignalKind.ForegroundChanged, at.AddMinutes(1), window with { Title = "Second document" }));
        await recorder.StopAsync();
        first = Assert.Single((await reports.ReadAsync(new(2026, 10, 19))).Intervals);
        Assert.Equal(report.Intervals[0].Id, first.Id);
        Assert.Equal(at, first.Start);
        Assert.Equal(TimeSpan.FromMinutes(1), first.Duration);
        Assert.False(first.IsOpen);
    }

    [Fact]
    public async Task FirstActivityAcrossLocalMidnightKeepsShortDayFragmentsAndIdentity()
    {
        var store = CreateStore();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        var midnight = new DateTimeOffset(2026, 10, 18, 22, 0, 0, TimeSpan.Zero);
        var at = midnight.AddSeconds(-10);
        var source = new IconSource
        {
            StartupSignals = [new(SignalKind.SessionActive, at),
                new(SignalKind.ForegroundChanged, at, new(100, 42, "editor", "First document"))]
        };
        var options = new ActivityTrackingOptions();
        await using var recorder = new Recorder(store, source, new IconClock(at), options);
        await recorder.StartAsync();
        source.Publish!(new(SignalKind.Heartbeat, midnight.AddSeconds(5)));
        await recorder.DeleteBeforeAsync(DateTimeOffset.MinValue);
        var reports = new DailyReportService(store, zone, options);
        var before = Assert.Single((await reports.ReadAsync(new(2026, 10, 18))).Intervals);
        var after = Assert.Single((await reports.ReadAsync(new(2026, 10, 19))).Intervals);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(at, before.Start);
        Assert.Equal(midnight, after.Start);
        Assert.Equal(TimeSpan.FromSeconds(10), before.Duration);
        Assert.Equal(TimeSpan.FromSeconds(5), after.Duration);
        Assert.Equal(new DateOnly[] { new(2026, 10, 18), new(2026, 10, 19) }, await reports.ReadActivityDatesAsync());
    }

    [Fact]
    public async Task ActivityDatesUseLocalSpansExcludeGapsAndFollowRetention()
    {
        var store = CreateStore();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        var reports = new DailyReportService(store, zone);
        Assert.Empty(await reports.ReadActivityDatesAsync());
        // The local midnight boundary is 22:00 UTC before the autumn DST transition.
        var at = new DateTimeOffset(2026, 10, 17, 21, 59, 40, TimeSpan.Zero);
        var leaf = new ActivityInterval(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"),
            at, at.AddDays(3).AddMinutes(1), false)
        {
            Spans = [
                new(Guid.NewGuid(), at, at.AddSeconds(20)),
                new(Guid.NewGuid(), at.AddDays(3), at.AddDays(3).AddMinutes(1))]
        };
        await store.SaveAsync(Composite(leaf));
        Assert.Equal(new DateOnly[] { new(2026, 10, 17), new(2026, 10, 20), new(2026, 10, 21) },
            await reports.ReadActivityDatesAsync());
        // A zero-length observation must not enable an otherwise empty date.
        var zero = leaf with { Id = Guid.NewGuid(), Start = at.AddDays(1), End = at.AddDays(1), Spans = [] };
        await store.SaveAsync(Composite(zero));
        Assert.Equal(3, (await reports.ReadActivityDatesAsync()).Count);
        await store.DeleteBeforeAsync(at.AddDays(4));
        Assert.Empty(await reports.ReadActivityDatesAsync());
    }

    [Fact]
    public async Task SettingsMigrationPreservesHistoryAndRoundTripsAllValues()
    {
        var store = CreateStore();
        var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={store.DatabasePath}").Options;
        Directory.CreateDirectory(_directory);
        await using (var db = new ActivityDbContext(options))
        {
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync("20261016191818_AdoptExistingSchema");
        }
        await store.InitializeAsync();
        Assert.Null(await store.ReadSettingsAsync());
        var at = DateTimeOffset.UnixEpoch;
        var interval = new ActivityInterval(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"), at, at.AddSeconds(30), false);
        await store.SaveAsync(Composite(interval));
        // Simulate the preceding release with populated history and no settings table.
        await using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE settings; DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_AddApplicationSettings';";
            await command.ExecuteNonQueryAsync();
        }
        store = CreateStore();
        await store.InitializeAsync();
        Assert.Null(await store.ReadSettingsAsync());
        foreach (var seconds in new[] { 0, 600, 15 })
        {
            var settings = new ApplicationSettings(seconds, true, false, false, ApplicationTheme.Dark, false,
                510, 1440, WorkDays.Tuesday | WorkDays.Sunday, true, 30);
            await store.SaveSettingsAsync(settings);
            Assert.Equal(settings, await CreateStore().ReadSettingsAsync());
        }
        var actual = Assert.Single(await store.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.Equal(interval.Id, actual.Id);
        Assert.Equal(interval.Duration, actual.Duration);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveSettingsAsync(new(-1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveSettingsAsync(new(601)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveSettingsAsync(new(Theme: (ApplicationTheme)99)));
        Assert.Equal(15, (await store.ReadSettingsAsync())!.MinimumActivitySeconds);
    }

    [Fact]
    public async Task RetentionRemovesOnlyExpiredClosedHistoryAndCascadesSpans()
    {
        var store = CreateStore();
        var cutoff = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        ActivityInterval Leaf(DateTimeOffset start, DateTimeOffset end, bool open = false) =>
            new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"), start, end, open);
        var expired = Leaf(cutoff.AddDays(-2), cutoff.AddMilliseconds(-1));
        var boundary = Leaf(cutoff.AddHours(-1), cutoff);
        var crossing = Leaf(cutoff.AddDays(-2), cutoff.AddHours(1)) with
        {
            Spans = [new(Guid.NewGuid(), cutoff.AddDays(-2), cutoff.AddDays(-2).AddMinutes(1)),
                new(Guid.NewGuid(), cutoff.AddMinutes(-1), cutoff.AddHours(1))]
        };
        var open = Leaf(cutoff.AddDays(-2), cutoff.AddHours(-1), true);
        var recent = Leaf(cutoff.AddHours(1), cutoff.AddHours(2));
        var shared = Leaf(cutoff.AddDays(-1), cutoff.AddHours(-1));
        foreach (var leaf in new[] { expired, boundary, crossing, open, recent })
            await store.SaveAsync(Composite(leaf));
        // An expired child of a retained, open parent must also be removed.
        await store.SaveAsync(new([], [shared with { ProcessActivityId = open.Id, WindowActivityId = open.Id }]));
        var oldEvent = new ActivityEvent(Guid.NewGuid(), expired.RecordingId, EventKind.Heartbeat, cutoff.AddMilliseconds(-1));
        var keptEvent = oldEvent with { Id = Guid.NewGuid(), Timestamp = cutoff };
        await store.SaveAsync(new([oldEvent, keptEvent], []));
        var settings = new ApplicationSettings(ActivityRetentionDays: 30);
        await store.SaveSettingsAsync(settings);
        await store.DeleteBeforeAsync(cutoff);
        await store.DeleteBeforeAsync(cutoff);
        var actual = await store.ReadIntervalsAsync(cutoff.AddDays(-3), cutoff.AddDays(1));
        Assert.Equal(new[] { boundary.Id, crossing.Id, open.Id, recent.Id }.Order(), actual.Select(x => x.Id).Order());
        Assert.Equal(crossing.Spans, actual.Single(x => x.Id == crossing.Id).Spans);
        Assert.True(actual.Single(x => x.Id == open.Id).IsOpen);
        Assert.Equal(keptEvent, Assert.Single(await store.ReadEventsAsync(cutoff.AddDays(-3), cutoff.AddDays(1))));
        Assert.Equal(settings, await store.ReadSettingsAsync());
        await using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM activity_spans WHERE activity_id IN ($expired, $shared);";
        command.Parameters.AddWithValue("$expired", expired.Id.ToString());
        command.Parameters.AddWithValue("$shared", shared.Id.ToString());
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task RetentionDuringRecordingDoesNotRestoreDeletedLeavesOnNextHeartbeat()
    {
        var store = CreateStore();
        var at = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        var source = new IconSource();
        await using var recorder = new Recorder(store, source, new IconClock(at), new ActivityTrackingOptions());
        await recorder.StartAsync();
        source.Publish!(new(SignalKind.SessionActive, at));
        source.Publish(new(SignalKind.ForegroundChanged, at, new(1, 2, "app", "old")));
        source.Publish(new(SignalKind.Heartbeat, at.AddMinutes(1)));
        source.Publish(new(SignalKind.ForegroundChanged, at.AddMinutes(2), new(1, 2, "app", "current")));
        source.Publish(new(SignalKind.Heartbeat, at.AddDays(2)));
        await recorder.DeleteBeforeAsync(at.AddDays(1));
        source.Publish(new(SignalKind.Heartbeat, at.AddDays(2).AddMinutes(1)));
        await recorder.StopAsync();
        var leaf = Assert.Single(await store.ReadIntervalsAsync(at, at.AddDays(3)));
        Assert.Equal("current", leaf.Window.Title);
        Assert.Equal(at.AddMinutes(2), leaf.Start);
        Assert.False(leaf.IsOpen);
    }

    [Fact]
    public async Task RoundTripsEventsAndIntervalsWithQuotedUnicodeTitles()
    {
        var store = CreateStore();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var window = new WindowInfo(123, 4, "editor", "Ĺ˝ltĂ˝ 'dokument'; DROP TABLE events;", "https://example.com/?a='b'");
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
        await store.DeleteBeforeAsync(DateTimeOffset.UnixEpoch.AddDays(1));
        Assert.Empty(await CreateStore().ReadProcessesAsync(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)));
        await using var check = new SqliteConnection($"Data Source={store.DatabasePath}");
        await check.OpenAsync();
        using var legacy = check.CreateCommand();
        legacy.CommandText = "SELECT COUNT(*) FROM intervals;";
        Assert.Equal(0L, await legacy.ExecuteScalarAsync());
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
            await RestoreLegacyProcessLayoutAsync(connection);
            command.CommandText = "DROP TABLE settings; DROP TABLE activity_spans; DELETE FROM __EFMigrationsHistory; PRAGMA user_version=2;";
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
            await RestoreLegacyProcessLayoutAsync(connection);
            command.CommandText = "DROP TABLE settings; DROP TABLE application_icons; DELETE FROM __EFMigrationsHistory; PRAGMA user_version=3;";
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
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
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
            await RestoreLegacyProcessLayoutAsync(connection);
            command.CommandText = "DROP TABLE settings; DROP TABLE __EFMigrationsHistory;";
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

    [Fact]
    public async Task RecordingBaselinesNeverBecomeSessionSeparatorsOrEnableEmptyHistoryDays()
    {
        var store = CreateStore();
        var at = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        ActivityEvent Entry(EventKind kind, DateTimeOffset time, string? reason = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), kind, time, Reason: reason);
        var baselineOnlyDay = Entry(EventKind.SessionInactive, at.AddDays(-1), "RecordingBaseline");
        var started = Entry(EventKind.RecordingStarted, at);
        var baseline = Entry(EventKind.SessionActive, at, "RecordingBaseline");
        var restarted = Entry(EventKind.SessionActive, at.AddMinutes(5), "RecordingBaseline");
        var lockEvent = Entry(EventKind.SessionInactive, at.AddMinutes(10), "WTS:7");
        var unlockEvent = Entry(EventKind.SessionActive, at.AddMinutes(11), "WTS:8");
        var unknownTransition = Entry(EventKind.SessionInactive, at.AddMinutes(15), "DesktopState");
        var entries = new[] { baselineOnlyDay, started, baseline, restarted, lockEvent, unlockEvent, unknownTransition };
        await store.SaveAsync(new(entries, []));
        var reports = new DailyReportService(store, TimeZoneInfo.Utc);
        Assert.Equal(new[] { lockEvent, unlockEvent, unknownTransition },
            (await reports.ReadAsync(new(2026, 10, 19))).SessionEvents);
        Assert.Empty((await reports.ReadAsync(new(2026, 10, 18))).SessionEvents);
        Assert.Equal(new DateOnly[] { new(2026, 10, 19) }, await reports.ReadActivityDatesAsync());
        // Filtering is a report concern: preserve every stored event for diagnostics.
        Assert.Equal(entries, await store.ReadEventsAsync(at.AddDays(-1), at.AddDays(1)));
    }

    [Fact]
    public async Task ApplicationStartupObservationAndShutdownSurvivePersistenceWithoutAddingWorkTime()
    {
        var store = CreateStore();
        var at = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
        var source = new IconSource
        {
            StartupSignals = [new(SignalKind.SessionActive, at, Reason: "ApplicationStartup")]
        };
        await using var recorder = new Recorder(store, source, new IconClock(at));
        await recorder.StartAsync();
        source.Publish!(new(SignalKind.SessionInactive, at, Reason: "DesktopState"));
        await recorder.StopForSessionEndAsync(false);
        var reports = new DailyReportService(store, TimeZoneInfo.Utc);
        var report = await reports.ReadAsync(new(2026, 10, 20));
        Assert.Equal(new[] { "ApplicationStartup", "DesktopState", "Shutdown" },
            report.SessionEvents.Select(x => x.Reason));
        Assert.Empty(report.Intervals);
        Assert.Equal(TimeSpan.Zero, report.ActiveDuration);
        Assert.Equal(new DateOnly[] { new(2026, 10, 20) }, await reports.ReadActivityDatesAsync());
    }

    [Fact]
    public async Task SessionEventsRespectLocalDayAndDoNotAddWorkTime()
    {
        var store = CreateStore();
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");
        var from = new DateTimeOffset(2026, 10, 18, 22, 0, 0, TimeSpan.Zero);
        ActivityEvent Entry(EventKind kind, DateTimeOffset at, string reason) =>
            new(Guid.NewGuid(), Guid.NewGuid(), kind, at, Reason: reason);
        var before = Entry(EventKind.SessionInactive, from.AddMilliseconds(-1), "WTS:7");
        var login = Entry(EventKind.SessionActive, from, "WTS:5");
        var locked = Entry(EventKind.SessionInactive, from.AddHours(8), "WTS:7");
        var next = Entry(EventKind.SessionActive, from.AddDays(1), "WTS:8");
        await store.SaveAsync(new([before, login, Entry(EventKind.Heartbeat, from.AddSeconds(1), ""), locked, next], []));
        var service = new DailyReportService(store, zone);
        var report = await service.ReadAsync(new(2026, 10, 19));
        Assert.Equal(new[] { login, locked }, report.SessionEvents);
        Assert.Empty(report.Intervals);
        Assert.Equal(TimeSpan.Zero, report.ActiveDuration);
        Assert.Null(report.FirstActivity);
        Assert.Equal(new[] { new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 20) },
            await service.ReadActivityDatesAsync());
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
        await Assert.ThrowsAsync<SqliteException>(() => store.InitializeAsync());
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

    [Theory]
    [InlineData(100L, 100L, 1)]
    [InlineData(100L, 200L, 2)]
    [InlineData(null, null, 3)]
    [InlineData(100L, null, 3)]
    public async Task RecorderReusesOnlyKnownProcessIdentityAcrossPauseAndRestart(long? firstTicks, long? nextTicks, int count)
    {
        var at = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        var store = CreateStore();
        var source = new IconSource();
        async Task Record(Recorder recorder, IconSource capture, int visit, long? ticks)
        {
            var start = at.AddSeconds(visit * 25);
            await recorder.StartAsync();
            capture.Publish!(new(SignalKind.SessionActive, start));
            capture.Publish(new(SignalKind.ForegroundChanged, start, new(100, 42, "editor", "Document", ProcessStartTicks: ticks)));
            capture.Publish(new(SignalKind.Heartbeat, start.AddSeconds(20)));
            await recorder.StopAsync();
        }
        Guid originalId;
        await using (var recorder = new Recorder(store, source, new IconClock(at)))
        {
            await Record(recorder, source, 0, firstTicks);
            originalId = Assert.Single(await store.ReadProcessesAsync(at, at.AddDays(1))).Id;
            await Record(recorder, source, 1, nextTicks);
        }
        // A new store and Recorder model restarting SolidRecords, without an in-memory identity cache.
        store = CreateStore();
        source = new IconSource();
        await using (var recorder = new Recorder(store, source, new IconClock(at.AddSeconds(50))))
            await Record(recorder, source, 2, nextTicks);
        var processes = await store.ReadProcessesAsync(at, at.AddDays(1));
        Assert.Equal(count, processes.Count);
        Assert.Contains(processes, p => p.Id == originalId);
        var report = new DailyReport(processes);
        Assert.Equal(TimeSpan.FromSeconds(60), report.ActiveDuration);
        Assert.Equal(3, report.Intervals.Count);
        Assert.Equal(3, report.Intervals.Select(x => x.RecordingId).Distinct().Count());
        Assert.Equal(3, report.Intervals.Select(x => x.Id).Distinct().Count());
        Assert.All(report.Intervals, leaf => Assert.Equal(TimeSpan.FromSeconds(20), leaf.Duration));
        if (count == 1)
        {
            var process = Assert.Single(processes);
            Assert.Equal(originalId, process.Id);
            Assert.Equal(at, process.Start);
            Assert.Equal(at.AddSeconds(70), process.End);
            Assert.All(report.Intervals, leaf => Assert.Equal(originalId, leaf.ProcessActivityId));
        }
    }

    [Fact]
    public async Task ProcessIdentityMigrationPreservesActivitiesRecordingsAndSpans()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        var options = new DbContextOptionsBuilder<ActivityDbContext>().UseSqlite($"Data Source={store.DatabasePath}").Options;
        await using (var db = new ActivityDbContext(options))
            await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync("20261016203531_AddApplicationSettings");
        var at = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        var processIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var recordings = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        await using var connection = new SqliteConnection($"Data Source={store.DatabasePath};Foreign Keys=True");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        for (var i = 0; i < processIds.Length; i++)
        {
            command.CommandText = """
                INSERT INTO process_activities VALUES($id,$recording,42,'editor',$ticks,$start,$end,1);
                INSERT INTO window_activities VALUES($id,$id,100,'Document',$start,$end,1);
                INSERT INTO activity_leaves VALUES($id,$id,'Document',NULL,$start,$end,1);
                INSERT INTO activity_spans VALUES($id,$id,$start,$end);
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", processIds[i].ToString());
            command.Parameters.AddWithValue("$recording", recordings[i].ToString());
            command.Parameters.AddWithValue("$ticks", i < 3 ? 100L : i == 3 ? 200L : DBNull.Value);
            command.Parameters.AddWithValue("$start", at.AddSeconds(i * 25).ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$end", at.AddSeconds(i * 25 + 20).ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync();
        }
        command.CommandText = "PRAGMA user_version=4;";
        await command.ExecuteNonQueryAsync();
        await store.InitializeAsync();
        var processes = await store.ReadProcessesAsync(at, at.AddDays(1));
        Assert.Equal(4, processes.Count);
        var combined = Assert.Single(processes, p => p.ProcessStartTicks == 100);
        Assert.Equal(processIds[0], combined.Id);
        Assert.Equal(at, combined.Start);
        Assert.Equal(at.AddSeconds(70), combined.End);
        Assert.Equal(3, combined.Windows.Count);
        var report = new DailyReport(processes);
        Assert.Equal(TimeSpan.FromSeconds(120), report.ActiveDuration);
        Assert.Equal(processIds.Order(), report.Intervals.Select(x => x.Id).Order());
        foreach (var leaf in report.Intervals)
        {
            var index = Array.IndexOf(processIds, leaf.Id);
            Assert.Equal(recordings[index], leaf.RecordingId);
            Assert.Equal(processIds[index], leaf.WindowActivityId);
            Assert.Equal(processIds[index], Assert.Single(leaf.Spans).Id);
            Assert.Equal(at.AddSeconds(index * 25), leaf.Start);
            Assert.False(leaf.IsOpen);
        }
        Assert.Equal(combined.Id, (await store.FindProcessAsync(42, 100))!.Id);
        command.Parameters.Clear();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using (var reader = await command.ExecuteReaderAsync()) Assert.False(await reader.ReadAsync());
        // Recovery and migration are idempotent; they never reconstruct deleted duplicate parents.
        Assert.Equal(4, (await CreateStore().ReadProcessesAsync(at, at.AddDays(1))).Count);
    }

    private static async Task RestoreLegacyProcessLayoutAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE process_activities SET recording_id = (
                SELECT l.recording_id FROM activity_leaves l JOIN window_activities w ON w.id=l.window_activity_id
                WHERE w.process_activity_id=process_activities.id LIMIT 1);
            DROP INDEX ix_process_identity;
            ALTER TABLE activity_leaves DROP COLUMN recording_id;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class IconClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private sealed class IconSource : IActivitySource
    {
        public IReadOnlyList<CaptureSignal> StartupSignals { get; init; } = [];
        public Action<CaptureSignal>? Publish { get; private set; }
        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default)
        {
            Publish = publish;
            foreach (var signal in StartupSignals) publish(signal);
            return Task.CompletedTask;
        }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static RecordingBatch Composite(ActivityInterval leaf)
    {
        var process = new ProcessActivity(leaf.Id, leaf.Window.ProcessId,
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
