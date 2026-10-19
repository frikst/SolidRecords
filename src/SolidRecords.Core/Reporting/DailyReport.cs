using SolidRecords.Core.Recording;

namespace SolidRecords.Core.Reporting;

public sealed class DailyReport
{
    public DailyReport(IReadOnlyList<ProcessActivity> processes, ActivityTrackingOptions? options = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var current = options ?? ActivityTrackingOptions.Default;
        var snapshot = new ActivityTrackingOptions(current.MinimumActivityDuration, current.MaximumMergeGap);
        var normalized = ActivityLogPolicy.MergeAdjacent(processes.SelectMany(p => p.Windows).SelectMany(w => w.Activities),
            snapshot);
        Intervals = normalized.Select(interval => ActivityLogPolicy.Clip(interval,
            from ?? DateTimeOffset.MinValue, to ?? DateTimeOffset.MaxValue)).OfType<ActivityInterval>()
            .Where(interval => interval.Duration > TimeSpan.Zero).ToArray();
        var byWindow = Intervals.ToLookup(interval => interval.WindowActivityId);
        Processes = processes.Select(process => process with
        {
            Windows = process.Windows.Select(window => window with { Activities = byWindow[window.Id].ToArray() })
                .Where(window => window.Activities.Count > 0).ToArray()
        }).Where(process => process.Windows.Count > 0).ToArray();
    }

    public IReadOnlyList<ProcessActivity> Processes { get; }
    public IReadOnlyList<ActivityInterval> Intervals { get; }
    public IReadOnlyList<ActivityEvent> SessionEvents { get; init; } = [];
    public TimeSpan ActiveDuration => TimeSpan.FromTicks(Intervals.Sum(x => x.Duration.Ticks));
    public DateTimeOffset? FirstActivity => Intervals.Count == 0 ? null : Intervals.Min(x => x.Start);
    public DateTimeOffset? LastActivity => Intervals.Count == 0 ? null : Intervals.Max(x => x.End);
}

public sealed class DailyReportService(IActivityStore store, TimeZoneInfo? timeZone = null, ActivityTrackingOptions? options = null)
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;

    // The collector needs its initial state to gate foreground tracking. It does not
    // represent an OS session transition, including when reading older recordings.
    private static bool IsSessionTransition(ActivityEvent entry) =>
        entry.Kind is EventKind.SessionActive or EventKind.SessionInactive && entry.Reason != "RecordingBaseline";

    public async Task<IReadOnlyList<DateOnly>> ReadActivityDatesAsync(CancellationToken cancellationToken = default)
    {
        var dates = new SortedSet<DateOnly>();
        foreach (var span in await store.ReadActivitySpansAsync(cancellationToken))
        {
            if (span.End <= span.Start) continue;
            var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(span.Start, _timeZone).DateTime);
            // End is exclusive: an activity ending at midnight does not enable the next day.
            var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(span.End.AddTicks(-1), _timeZone).DateTime);
            for (var date = first; ; date = date.AddDays(1))
            {
                dates.Add(date);
                if (date >= last) break;
            }
        }
        foreach (var entry in (await store.ReadSessionEventsAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, cancellationToken))
            .Where(IsSessionTransition))
            dates.Add(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.Timestamp, _timeZone).DateTime));
        return dates.ToArray();
    }

    public async Task<DailyReport> ReadAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, _timeZone));
        var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, _timeZone));
        var processes = await store.ReadProcessesAsync(from, to, cancellationToken);
        var events = await store.ReadSessionEventsAsync(from, to, cancellationToken);
        return new(processes, options, from, to)
        {
            SessionEvents = events.Where(IsSessionTransition).ToArray()
        };
    }
}
