namespace SolidRecords.Core;

public sealed record DailyReport(IReadOnlyList<ActivityInterval> Intervals, IReadOnlyList<ActivityEvent> Events)
{
    public TimeSpan ActiveDuration => TimeSpan.FromTicks(Intervals.Sum(x => x.Duration.Ticks));
    public DateTimeOffset? FirstActivity => Intervals.Count == 0 ? null : Intervals.Min(x => x.Start);
    public DateTimeOffset? LastActivity => Intervals.Count == 0 ? null : Intervals.Max(x => x.End);
}

public sealed class DailyReportService(IActivityStore store, TimeZoneInfo? timeZone = null)
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;

    public async Task<DailyReport> ReadAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        // Convert both midnights separately; a local day can contain 23 or 25 hours.
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, _timeZone));
        var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, _timeZone));
        var intervals = await store.ReadIntervalsAsync(from, to, cancellationToken);
        var events = await store.ReadEventsAsync(from, to, cancellationToken);
        return new(intervals.Where(x => x.Duration > TimeSpan.Zero).ToArray(),
            events.Where(x => x.Kind != EventKind.Heartbeat).ToArray());
    }
}
