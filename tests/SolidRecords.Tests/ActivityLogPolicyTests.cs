using SolidRecords.Core;
using SolidRecords.Core.Recording;
using Xunit;

namespace SolidRecords.Tests;

public sealed class ActivityLogPolicyTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recording = Guid.NewGuid();
    private static readonly Guid Process = Guid.NewGuid();
    private static readonly Guid Window = Guid.NewGuid();
    private static readonly WindowInfo Info = new(100, 42, "browser", "Document", "https://example.com/a");

    private static ActivityInterval Visit(double start, double end, WindowInfo? info = null, bool open = false) =>
        new(Guid.NewGuid(), Recording, info ?? Info, At.AddSeconds(start), At.AddSeconds(end), open, Process, Window);

    [Fact]
    public void DefaultsShareTheRequestedThresholdAndCanBeOverriddenIndependently()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), ActivityTrackingOptions.Default.MinimumActivityDuration);
        Assert.Equal(TimeSpan.FromSeconds(15), ActivityTrackingOptions.Default.MaximumMergeGap);
        var options = new ActivityTrackingOptions(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(20), options.MinimumActivityDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), options.MaximumMergeGap);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityTrackingOptions(TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(15, 14, false)]
    [InlineData(15, 15, true)]
    [InlineData(600, 599, false)]
    [InlineData(600, 600, true)]
    public void ConfiguredMinimumIsSharedByRecordingAndReports(int minimum, int duration, bool retained)
    {
        var options = new ActivityTrackingOptions();
        options.SetMinimumActivityDuration(TimeSpan.FromSeconds(minimum));
        var visit = Visit(0, duration);
        var batch = new ActivityLogPolicy(options).Apply(new([], [visit]));
        var window = new WindowActivity(Window, Process, Info.Handle, Info.Title, visit.Start, visit.End, false) { Activities = [visit] };
        var process = new ProcessActivity(Process, Recording, Info.ProcessId, Info.Application, null, visit.Start, visit.End, false) { Windows = [window] };
        var report = new SolidRecords.Core.Reporting.DailyReport([process], options);
        Assert.Equal(retained ? 1 : 0, batch.Intervals.Count);
        Assert.Equal(retained ? 1 : 0, report.Intervals.Count);
        Assert.Equal(TimeSpan.FromSeconds(15), options.MaximumMergeGap);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(14.999, false)]
    [InlineData(15, true)]
    [InlineData(15.001, true)]
    public void MinimumDurationHasAnInclusiveBoundary(double seconds, bool retained)
    {
        var policy = new ActivityLogPolicy();
        var result = policy.Apply(new([], [Visit(0, seconds)]));
        Assert.Equal(retained ? 1 : 0, result.Intervals.Count);
    }

    [Fact]
    public void OpenCandidateAppearsOnlyAtThresholdWithItsOriginalStart()
    {
        var policy = new ActivityLogPolicy();
        var candidate = Visit(0, 0, open: true);
        Assert.Empty(policy.Apply(new([], [candidate])).Intervals);
        Assert.Empty(policy.Apply(new([], [candidate with { End = At.AddSeconds(14) }])).Intervals);
        var kept = Assert.Single(policy.Apply(new([], [candidate with { End = At.AddSeconds(15) }])).Intervals);
        Assert.Equal(candidate.Id, kept.Id);
        Assert.Equal(At, kept.Start);
        Assert.True(kept.IsOpen);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(14.999, 1)]
    [InlineData(15, 2)]
    [InlineData(15.001, 2)]
    public void MergeGapHasAStrictBoundary(double gap, int expectedCount)
    {
        var first = Visit(0, 20);
        var second = Visit(20 + gap, 40 + gap);
        var result = ActivityLogPolicy.MergeAdjacent([first, second], ActivityTrackingOptions.Default);
        Assert.Equal(expectedCount, result.Count);
        if (expectedCount == 1)
        {
            Assert.Equal(first.Id, result[0].Id);
            Assert.Equal(TimeSpan.FromSeconds(40), result[0].Duration);
            Assert.Equal(2, result[0].Spans.Count);
        }
    }

    [Fact]
    public void DiscardedShortVisitDoesNotBreakAdjacencyButItsTimeIsNotCounted()
    {
        var first = Visit(0, 20);
        var ignored = Visit(20, 25, Info with { Title = "Brief other activity" });
        var last = Visit(25, 45);
        var result = Assert.Single(ActivityLogPolicy.MergeAdjacent([first, ignored, last], ActivityTrackingOptions.Default));
        Assert.Equal(TimeSpan.FromSeconds(40), result.Duration);
        Assert.Equal(At, result.Start);
        Assert.Equal(At.AddSeconds(45), result.End);
    }

    [Fact]
    public void SeparateShortVisitsCannotAccumulateIntoAQualifiedActivity()
    {
        Assert.Empty(ActivityLogPolicy.MergeAdjacent([Visit(0, 10), Visit(11, 21)], ActivityTrackingOptions.Default));
    }

    [Fact]
    public void RetainedDifferentVisitPreventsMergingAcrossIt()
    {
        var result = ActivityLogPolicy.MergeAdjacent([Visit(0, 20),
            Visit(20, 35, Info with { Url = "https://example.com/b" }), Visit(35, 55)], ActivityTrackingOptions.Default);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void DifferentParentsTitlesAndUrlsRemainSeparate()
    {
        var first = Visit(0, 20);
        var next = Visit(21, 41);
        foreach (var different in new[]
        {
            next with { RecordingId = Guid.NewGuid() }, next with { ProcessActivityId = Guid.NewGuid() },
            next with { WindowActivityId = Guid.NewGuid() }, next with { Window = Info with { Title = "Other" } },
            next with { Window = Info with { Url = "https://example.com/b" } },
            next with { Window = Info with { ProcessId = 43 } }
        }) Assert.Equal(2, ActivityLogPolicy.MergeAdjacent([first, different], ActivityTrackingOptions.Default).Count);
    }

    [Fact]
    public void LateMetadataCanMergePreviouslyRetainedRowsAndRequestRemovalOfTheDuplicate()
    {
        var policy = new ActivityLogPolicy();
        var first = Visit(0, 20);
        var next = Visit(25, 40, Info with { Url = null }, open: true);
        policy.Apply(new([], [first]));
        Assert.Equal(next.Id, Assert.Single(policy.Apply(new([], [next])).Intervals).Id);
        var enriched = policy.Apply(new([], [next with { Window = Info, End = At.AddSeconds(41) }]));
        var merged = Assert.Single(enriched.Intervals);
        Assert.Equal(first.Id, merged.Id);
        Assert.Contains(next.Id, enriched.RemovedIntervalIds);
        Assert.Equal(TimeSpan.FromSeconds(36), merged.Duration);
    }

    [Fact]
    public void MetadataThatSeparatesAMergeRestoresTheCorrectSpanOwnership()
    {
        var policy = new ActivityLogPolicy();
        var unknown = Info with { Url = null };
        var first = Visit(0, 20, unknown);
        var next = Visit(25, 40, unknown, true);
        policy.Apply(new([], [first]));
        Assert.Equal(first.Id, Assert.Single(policy.Apply(new([], [next])).Intervals).Id);
        var split = policy.Apply(new([], [next with { Window = Info }]));
        Assert.Equal(2, split.Intervals.Count);
        Assert.All(split.Intervals, row => Assert.Single(row.Spans));
    }

    [Fact]
    public void DayClippingUsesSpansAndDoesNotCountTheGapOrReapplyTheMinimumToDayFragments()
    {
        var midnight = At;
        var merged = Assert.Single(ActivityLogPolicy.MergeAdjacent([Visit(-20, -1), Visit(5, 25)], ActivityTrackingOptions.Default));
        var before = ActivityLogPolicy.Clip(merged, midnight.AddDays(-1), midnight)!;
        var after = ActivityLogPolicy.Clip(merged, midnight, midnight.AddDays(1))!;
        Assert.Equal(TimeSpan.FromSeconds(19), before.Duration);
        Assert.Equal(TimeSpan.FromSeconds(20), after.Duration);
        Assert.Equal(midnight.AddSeconds(5), after.Start);
    }
}
