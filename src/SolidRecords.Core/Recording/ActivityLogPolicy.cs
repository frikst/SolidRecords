namespace SolidRecords.Core.Recording;

/// <summary>Persistence policy over raw observations; short candidates exist only in capture memory.</summary>
public sealed class ActivityLogPolicy(ActivityTrackingOptions? options = null)
{
    private readonly ActivityTrackingOptions _options = options ?? ActivityTrackingOptions.Default;
    private readonly Dictionary<Guid, ActivityInterval> _accepted = [];
    private Dictionary<Guid, ActivityInterval> _published = [];

    public RecordingBatch Apply(RecordingBatch batch)
    {
        if (batch.Events.Any(e => e.Kind == EventKind.RecordingStarted))
        {
            _accepted.Clear();
            _published.Clear();
        }
        if (batch.Intervals.Count == 0) return batch;
        foreach (var interval in batch.Intervals)
            if (interval.Duration >= _options.MinimumActivityDuration) _accepted[interval.Id] = interval;

        var normalized = MergeAdjacent(_accepted.Values, _options).ToDictionary(interval => interval.Id);
        var changed = normalized.Values.Where(interval => !_published.TryGetValue(interval.Id, out var previous) ||
            !SameContents(previous, interval)).ToArray();
        var removed = _published.Keys.Where(id => !normalized.ContainsKey(id)).ToArray();
        _published = normalized;
        return batch with { Intervals = changed, RemovedIntervalIds = removed };
    }

    /// <summary>Normalize the chronological log before any GUI search or day clipping.</summary>
    public static IReadOnlyList<ActivityInterval> MergeAdjacent(IEnumerable<ActivityInterval> intervals, ActivityTrackingOptions options)
    {
        var result = new List<ActivityInterval>();
        foreach (var interval in intervals.Where(x => x.Duration >= options.MinimumActivityDuration).OrderBy(x => x.Start))
        {
            var previous = result.LastOrDefault();
            var gap = previous is null ? TimeSpan.MaxValue : interval.Start - previous.End;
            if (previous is not null && !previous.IsOpen && gap >= TimeSpan.Zero && gap < options.MaximumMergeGap &&
                previous.RecordingId == interval.RecordingId && previous.ProcessActivityId == interval.ProcessActivityId &&
                previous.WindowActivityId == interval.WindowActivityId && previous.Window == interval.Window)
            {
                result[^1] = previous with
                {
                    End = interval.End, IsOpen = interval.IsOpen,
                    Spans = GetSpans(previous).Concat(GetSpans(interval)).ToArray()
                };
            }
            else result.Add(interval with { Spans = GetSpans(interval) });
        }
        return result;
    }

    public static IReadOnlyList<ActivitySpan> GetSpans(ActivityInterval interval) => interval.Spans.Count == 0
        ? [new(interval.Id, interval.Start, interval.End)] : interval.Spans;

    public static ActivityInterval? Clip(ActivityInterval interval, DateTimeOffset from, DateTimeOffset to)
    {
        var spans = GetSpans(interval).Where(span => span.Start < to && span.End > from)
            .Select(span => span with { Start = span.Start < from ? from : span.Start, End = span.End > to ? to : span.End }).ToArray();
        if (spans.Length == 0) return null;
        return interval with
        {
            Start = spans[0].Start, End = spans[^1].End, Spans = spans,
            IsOpen = interval.IsOpen && interval.End <= to
        };
    }

    private static bool SameContents(ActivityInterval first, ActivityInterval second) =>
        first.Id == second.Id && first.Window == second.Window && first.Start == second.Start &&
        first.End == second.End && first.IsOpen == second.IsOpen && first.Spans.SequenceEqual(second.Spans);
}
