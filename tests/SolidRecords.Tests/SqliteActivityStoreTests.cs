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
        await store.SaveAsync(new([entry], [interval]));
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
        await store.SaveAsync(new([], [interval]));
        await store.SaveAsync(new([], [interval with { End = at.AddSeconds(5) }]));
        var recovered = CreateStore();
        await recovered.InitializeAsync();
        var actual = Assert.Single(await recovered.ReadIntervalsAsync(at, at.AddDays(1)));
        Assert.False(actual.IsOpen);
        Assert.Equal(TimeSpan.FromSeconds(5), actual.Duration);
    }

    [Fact]
    public async Task DayBoundaryClipsIntervalsWithoutDoubleCounting()
    {
        var store = CreateStore();
        var midnight = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(new([], [new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"),
            midnight.AddMinutes(-5), midnight.AddMinutes(10), false)]));
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
        await store.SaveAsync(new([], [new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"), start, end, false)]));
        var report = await new DailyReportService(store, timeZone).ReadAsync(date);
        Assert.Equal(TimeSpan.FromHours(hours), report.ActiveDuration);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
