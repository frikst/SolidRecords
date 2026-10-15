using SolidRecords.Core;
using Xunit;

namespace SolidRecords.Tests;

public sealed class RecordingEngineTests
{
    private static readonly DateTimeOffset Origin = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly WindowInfo Browser = new(100, 42, "chrome", "Document A", "https://example.com/a");

    private static RecordingEngine Start()
    {
        var engine = new RecordingEngine();
        engine.Apply(new(SignalKind.RecordingStarted, Origin));
        engine.Apply(new(SignalKind.SessionActive, Origin));
        return engine;
    }

    [Fact]
    public void StartingWhileLockedRecordsInactiveBaselineOnlyOnce()
    {
        var engine = new RecordingEngine();
        engine.Apply(new(SignalKind.RecordingStarted, Origin));
        var baseline = engine.Apply(new(SignalKind.SessionInactive, Origin));
        Assert.Equal(EventKind.SessionInactive, Assert.Single(baseline.Events).Kind);
        Assert.Empty(engine.Apply(new(SignalKind.SessionInactive, Origin.AddSeconds(1))).Events);
        Assert.Empty(engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddSeconds(2), Browser)).Intervals);
    }

    [Fact]
    public void SwitchingWindowsClosesPreviousAndOpensNext()
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var editor = new WindowInfo(200, 50, "code", "Program.cs");
        var batch = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(5), editor));
        Assert.Equal([EventKind.WindowDeactivated, EventKind.WindowActivated], batch.Events.Select(x => x.Kind));
        Assert.Equal(TimeSpan.FromMinutes(5), batch.Intervals[0].Duration);
        Assert.False(batch.Intervals[0].IsOpen);
        Assert.True(batch.Intervals[1].IsOpen);
        Assert.Equal(editor, batch.Intervals[1].Window);
    }

    [Theory]
    [InlineData("Document B", "https://example.com/a")]
    [InlineData("Document A", "https://example.com/b")]
    public void TitleOrUrlChangesSplitTheDocumentWithoutFalseWindowActivation(string title, string url)
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var batch = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddSeconds(10), Browser with { Title = title, Url = url }));
        Assert.Equal(EventKind.DocumentChanged, Assert.Single(batch.Events).Kind);
        Assert.Equal(2, batch.Intervals.Count);
        Assert.False(batch.Intervals[0].IsOpen);
        Assert.True(batch.Intervals[1].IsOpen);
    }

    [Fact]
    public void DuplicateObservationsDoNotCreateExtraIntervals()
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var batch = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddSeconds(1), Browser));
        Assert.Empty(batch.Events);
        Assert.Empty(batch.Intervals);
    }

    [Fact]
    public void LockedTimeIsExcludedUntilUnlockAndNewForegroundObservation()
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var locked = engine.Apply(new(SignalKind.SessionInactive, Origin.AddMinutes(2)));
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(locked.Intervals).Duration);
        Assert.Empty(engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(5), Browser)).Intervals);
        Assert.Empty(engine.Apply(new(SignalKind.Heartbeat, Origin.AddMinutes(10))).Intervals);
        engine.Apply(new(SignalKind.SessionActive, Origin.AddMinutes(20)));
        var unlocked = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(20), Browser));
        Assert.Equal(Origin.AddMinutes(20), Assert.Single(unlocked.Intervals).Start);
    }

    [Fact]
    public void StopClosesCurrentAndIgnoresFurtherCapture()
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var stopped = engine.Apply(new(SignalKind.RecordingStopped, Origin.AddMinutes(3)));
        Assert.False(Assert.Single(stopped.Intervals).IsOpen);
        Assert.Equal(EventKind.RecordingStopped, stopped.Events[^1].Kind);
        Assert.Empty(engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(4), Browser)).Events);
    }

    [Fact]
    public void HeartbeatConfirmsEndAndDelayedEventsCannotCreateNegativeDurations()
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var heartbeat = engine.Apply(new(SignalKind.Heartbeat, Origin.AddSeconds(5)));
        Assert.Equal(Origin.AddSeconds(5), Assert.Single(heartbeat.Intervals).End);
        var stopped = engine.Apply(new(SignalKind.WindowClosed, Origin.AddSeconds(3), Browser));
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(stopped.Intervals).Duration);
    }

    [Fact]
    public void ForegroundDisappearanceDeactivatesWindow()
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var batch = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddSeconds(1)));
        Assert.Equal(EventKind.WindowDeactivated, Assert.Single(batch.Events).Kind);
        Assert.False(Assert.Single(batch.Intervals).IsOpen);
    }

    [Fact]
    public void BackgroundProcessStaysOpenButDoesNotAccumulateActiveTime()
    {
        var engine = Start();
        var first = engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var deactivated = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(2)));
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(deactivated.Intervals).Duration);
        Assert.DoesNotContain(deactivated.Processes, p => !p.IsOpen);
        var background = engine.Apply(new(SignalKind.Heartbeat, Origin.AddMinutes(10)));
        Assert.Empty(background.Intervals);
        Assert.True(Assert.Single(background.Processes).IsOpen);
        var closed = engine.Apply(new(SignalKind.WindowClosed, Origin.AddMinutes(12), Browser));
        var process = Assert.Single(closed.Processes);
        Assert.Equal(Assert.Single(first.Processes).Id, process.Id);
        Assert.False(process.IsOpen);
        Assert.Equal(Origin.AddMinutes(12), process.End);
    }

    [Fact]
    public void ProcessEndsOnlyWhenLastWindowCloses()
    {
        var engine = Start();
        engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser));
        var second = Browser with { Handle = 101, Title = "Other window" };
        engine.Apply(new(SignalKind.WindowObserved, Origin.AddSeconds(1), second));
        var firstClosed = engine.Apply(new(SignalKind.WindowClosed, Origin.AddSeconds(2), Browser));
        Assert.DoesNotContain(firstClosed.Processes, p => !p.IsOpen);
        Assert.False(Assert.Single(firstClosed.Windows).IsOpen);
        var lastClosed = engine.Apply(new(SignalKind.WindowClosed, Origin.AddSeconds(3), second));
        Assert.False(Assert.Single(lastClosed.Processes).IsOpen);
    }

    [Fact]
    public void ReactivationAndUrlChangesKeepCompositeParentsAndCreateSeparateLeaves()
    {
        var engine = Start();
        var first = Assert.Single(engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser)).Intervals);
        engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(1)));
        var again = Assert.Single(engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(5), Browser)).Intervals);
        Assert.Equal(first.ProcessActivityId, again.ProcessActivityId);
        Assert.Equal(first.WindowActivityId, again.WindowActivityId);
        Assert.NotEqual(first.Id, again.Id);
        var changed = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddMinutes(6), Browser with { Url = "https://example.com/b" }));
        Assert.Equal(2, changed.Intervals.Count);
        Assert.All(changed.Intervals, leaf => Assert.Equal(first.WindowActivityId, leaf.WindowActivityId));
        Assert.Equal(TimeSpan.FromMinutes(1), changed.Intervals[0].Duration);
    }

    [Fact]
    public void ReusedPidDoesNotMergeDistinctProcessInstances()
    {
        var engine = Start();
        var first = engine.Apply(new(SignalKind.ForegroundChanged, Origin, Browser with { ProcessStartTicks = 100 }));
        var next = engine.Apply(new(SignalKind.ForegroundChanged, Origin.AddSeconds(1), Browser with { ProcessStartTicks = 200 }));
        Assert.NotEqual(first.Processes[0].Id, next.Processes[^1].Id);
    }
}
