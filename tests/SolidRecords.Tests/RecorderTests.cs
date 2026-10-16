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
        public bool Stopped { get; private set; }
        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default)
        {
            Publish = publish;
            return Fail ? Task.FromException(new InvalidOperationException("Capture failed")) : Task.CompletedTask;
        }
        public Task StopAsync() { Stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MemoryStore : IActivityStore
    {
        public List<ActivityEvent> Events { get; } = [];
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
