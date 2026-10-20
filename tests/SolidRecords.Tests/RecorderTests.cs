using SolidRecords.Core;
using SolidRecords.Core.Recording;
using Xunit;

namespace SolidRecords.Tests;

public sealed class RecorderTests
{
    [Fact]
    public async Task StopDrainsAllAcceptedEventsAndRestartCreatesNewRecording()
    {
        var store = new MemoryStore();
        var source = new FakeSource();
        await using var recorder = new Recorder(store, source);
        await recorder.StartAsync();
        source.Publish!(new(SignalKind.SessionActive, DateTimeOffset.UtcNow));
        for (var i = 0; i < 100; i++) source.Publish!(new(SignalKind.ForegroundChanged, DateTimeOffset.UtcNow,
            new(i + 1, i + 1, "app", "window")));
        await recorder.StopAsync();
        Assert.Equal(100, store.Events.Count(x => x.Kind == EventKind.WindowActivated));
        Assert.Equal(EventKind.RecordingStopped, store.Events[^1].Kind);
        Assert.False(recorder.IsRecording);
        await recorder.StartAsync();
        await recorder.StopAsync();
        Assert.Equal(2, store.Events.Where(x => x.Kind == EventKind.RecordingStarted).Select(x => x.RecordingId).Distinct().Count());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SessionEndPersistsKnownReasonAndDrainsSourceWithoutDuplicates(bool isLogoff, bool nativeFirst)
    {
        var store = new MemoryStore();
        var source = new FakeSource();
        await using var recorder = new Recorder(store, source);
        await recorder.StartAsync();
        source.Publish!(new(SignalKind.SessionActive, DateTimeOffset.UtcNow));
        source.Publish(new(SignalKind.SessionInactive, DateTimeOffset.UtcNow, Reason: "DesktopState"));
        var reason = isLogoff ? "Logoff" : "Shutdown";
        if (nativeFirst)
            source.Publish(new(SignalKind.SessionInactive, DateTimeOffset.UtcNow, Reason: reason));
        else
            source.OnStop = () => source.Publish(new(SignalKind.SessionInactive, DateTimeOffset.UtcNow, Reason: reason));
        await recorder.StopForSessionEndAsync(isLogoff);
        var ending = Assert.Single(store.Events, x => x.Reason == reason);
        Assert.Equal(EventKind.SessionInactive, ending.Kind);
        Assert.Equal(EventKind.RecordingStopped, store.Events[^1].Kind);
        await recorder.StopForSessionEndAsync(isLogoff);
        Assert.Single(store.Events, x => x.Reason == reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionEndWithoutNativeNotificationIsPersistedButPausedRecordingAddsNothing(bool isLogoff)
    {
        var store = new MemoryStore();
        var source = new FakeSource();
        await using var recorder = new Recorder(store, source);
        await recorder.StopForSessionEndAsync(isLogoff);
        Assert.Empty(store.Events);
        await recorder.StartAsync();
        source.Publish!(new(SignalKind.SessionActive, DateTimeOffset.UtcNow));
        await recorder.StopForSessionEndAsync(isLogoff);
        Assert.Equal(isLogoff ? "Logoff" : "Shutdown",
            Assert.Single(store.Events, x => x.Kind == EventKind.SessionInactive).Reason);
        Assert.Equal(EventKind.RecordingStopped, store.Events[^1].Kind);
    }

    [Fact]
    public async Task SourceStartupFailureIsStoppedAndPersistedAsStopped()
    {
        var store = new MemoryStore();
        var source = new FakeSource { Fail = true };
        await using var recorder = new Recorder(store, source);
        await Assert.ThrowsAsync<InvalidOperationException>(recorder.StartAsync);
        Assert.False(recorder.IsRecording);
        Assert.True(source.Stopped);
        Assert.Equal(EventKind.RecordingStopped, store.Events[^1].Kind);
    }

    private sealed class FakeSource : IActivitySource
    {
        public Action<CaptureSignal>? Publish { get; private set; }
        public bool Fail { get; init; }
        public Action? OnStop { get; set; }
        public bool Stopped { get; private set; }
        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default)
        {
            Publish = publish;
            return Fail ? Task.FromException(new InvalidOperationException("Capture failed")) : Task.CompletedTask;
        }
        public Task StopAsync() { Stopped = true; OnStop?.Invoke(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MemoryStore : IActivityStore
    {
        public List<ActivityEvent> Events { get; } = [];
        public Task DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ActivitySpan>> ReadActivitySpansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivitySpan>>([]);
        public Task<ProcessActivity?> FindProcessAsync(int processId, long processStartTicks, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProcessActivity?>(null);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(RecordingBatch batch, CancellationToken cancellationToken = default)
        {
            Events.AddRange(batch.Events);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ActivityInterval>> ReadIntervalsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityInterval>>([]);
        public Task<IReadOnlyList<ProcessActivity>> ReadProcessesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcessActivity>>([]);
        public Task<IReadOnlyList<ActivityEvent>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityEvent>>(Events.ToArray());
    }
}
