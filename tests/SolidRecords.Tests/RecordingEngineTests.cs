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
        var stopped = engine.Apply(new(SignalKind.ProcessStopped, Origin.AddSeconds(3), ProcessId: 42));
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
}
