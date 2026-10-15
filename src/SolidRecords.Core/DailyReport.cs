namespace SolidRecords.Core;

public sealed class DailyReport
{
    public DailyReport(IReadOnlyList<ProcessActivity> processes)
    {
        Processes = processes;
        Intervals = processes.SelectMany(p => p.Windows).SelectMany(w => w.Activities)
            .Where(x => x.Duration > TimeSpan.Zero).OrderBy(x => x.Start).ToArray();
    }

    public IReadOnlyList<ProcessActivity> Processes { get; }
    public IReadOnlyList<ActivityInterval> Intervals { get; }
    public TimeSpan ActiveDuration => TimeSpan.FromTicks(Intervals.Sum(x => x.Duration.Ticks));
    public DateTimeOffset? FirstActivity => Intervals.Count == 0 ? null : Intervals.Min(x => x.Start);
    public DateTimeOffset? LastActivity => Intervals.Count == 0 ? null : Intervals.Max(x => x.End);
}

public sealed class DailyReportService(IActivityStore store, TimeZoneInfo? timeZone = null)
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;

    public async Task<DailyReport> ReadAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, _timeZone));
        var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, _timeZone));
        return new(await store.ReadProcessesAsync(from, to, cancellationToken));
    }
}
