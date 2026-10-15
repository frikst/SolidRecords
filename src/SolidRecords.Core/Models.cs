namespace SolidRecords.Core;

public enum SignalKind
{
    RecordingStarted, RecordingStopped, SessionActive, SessionInactive,
    ProcessStarted, ProcessStopped, ProcessObserved, ForegroundChanged, Heartbeat, CaptureFailed
}

public enum EventKind
{
    RecordingStarted, RecordingStopped, SessionActive, SessionInactive,
    ProcessStarted, ProcessStopped, ProcessObserved, WindowActivated,
    WindowDeactivated, DocumentChanged, Heartbeat
}

public sealed record WindowInfo(long Handle, int ProcessId, string Application, string Title, string? Url = null);

public sealed record CaptureSignal(SignalKind Kind, DateTimeOffset Timestamp,
    WindowInfo? Window = null, int? ProcessId = null, string? Application = null, string? Reason = null);

public sealed record ActivityEvent(Guid Id, Guid RecordingId, EventKind Kind, DateTimeOffset Timestamp,
    WindowInfo? Window = null, int? ProcessId = null, string? Application = null, string? Reason = null);

// End is the last confirmed observation, even for an open interval. A crash can never
// turn an unfinished interval into hours of invented activity on the next launch.
public sealed record ActivityInterval(Guid Id, Guid RecordingId, WindowInfo Window,
    DateTimeOffset Start, DateTimeOffset End, bool IsOpen)
{
    public TimeSpan Duration => End - Start;
}

public sealed record RecordingBatch(IReadOnlyList<ActivityEvent> Events, IReadOnlyList<ActivityInterval> Intervals);

public interface IActivityStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RecordingBatch batch, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityInterval>> ReadIntervalsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityEvent>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public interface IActivitySource : IAsyncDisposable
{
    string? Warning { get; }
    Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default);
    Task StopAsync();
}
