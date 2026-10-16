using Microsoft.Data.Sqlite;
using SolidRecords.Core;
using SolidRecords.Storage;
using Xunit;

namespace SolidRecords.Tests;

public sealed class SqliteActivityStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SolidRecords.Tests", Guid.NewGuid().ToString());
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
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveAsync(new([entry, entry], [])));
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
