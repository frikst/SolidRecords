namespace SolidRecords.Core;

public enum SignalKind
{
    RecordingStarted, RecordingStopped, SessionActive, SessionInactive,
    ForegroundChanged, WindowObserved, WindowClosed, Heartbeat, CaptureFailed
}

public enum EventKind
{
    RecordingStarted, RecordingStopped, SessionActive, SessionInactive,
    ProcessStarted, ProcessStopped, ProcessObserved, WindowActivated,
    WindowDeactivated, DocumentChanged, Heartbeat
}

public sealed record WindowInfo(long Handle, int ProcessId, string Application, string Title, string? Url = null,
    long? ProcessStartTicks = null);

/// <summary>PNG metadata, independent of activity identity and timing.</summary>
public sealed record ApplicationIcon(string Application, string PngBase64);

public sealed record CaptureSignal(SignalKind Kind, DateTimeOffset Timestamp,
    WindowInfo? Window = null, int? ProcessId = null, string? Application = null, string? Reason = null,
    ApplicationIcon? Icon = null);

public sealed record ActivityEvent(Guid Id, Guid RecordingId, EventKind Kind, DateTimeOffset Timestamp,
    WindowInfo? Window = null, int? ProcessId = null, string? Application = null, string? Reason = null);

// End is the last confirmed observation, even for an open interval. A crash can never
// turn an unfinished interval into hours of invented activity on the next launch.
public sealed record ActivityInterval(Guid Id, Guid RecordingId, WindowInfo Window,
    DateTimeOffset Start, DateTimeOffset End, bool IsOpen,
    Guid ProcessActivityId = default, Guid WindowActivityId = default)
{
    public string? ApplicationIconPngBase64 { get; init; }
    public IReadOnlyList<ActivitySpan> Spans { get; init; } = [];
    public TimeSpan Duration => Spans.Count == 0 ? End - Start : TimeSpan.FromTicks(Spans.Sum(span => (span.End - span.Start).Ticks));
}

/// <summary>Actual foreground time. Gaps between spans are never counted as work.</summary>
public sealed record ActivitySpan(Guid Id, DateTimeOffset Start, DateTimeOffset End);

/// <summary>Observed lifetime, starting with the first foreground window and ending with the last closed window.</summary>
public sealed record ProcessActivity(Guid Id, Guid RecordingId, int ProcessId, string Application,
    long? ProcessStartTicks, DateTimeOffset Start, DateTimeOffset End, bool IsOpen)
{
    public IReadOnlyList<WindowActivity> Windows { get; init; } = [];
}

/// <summary>A window's lifetime. Only its leaf activities contribute to tracked time.</summary>
public sealed record WindowActivity(Guid Id, Guid ProcessActivityId, long Handle, string Title,
    DateTimeOffset Start, DateTimeOffset End, bool IsOpen)
{
    // A leaf with a URL represents a URL visit; a leaf without a URL represents active window time.
    public IReadOnlyList<ActivityInterval> Activities { get; init; } = [];
}

public sealed record RecordingBatch(IReadOnlyList<ActivityEvent> Events, IReadOnlyList<ActivityInterval> Intervals)
{
    public IReadOnlyList<ProcessActivity> Processes { get; init; } = [];
    public IReadOnlyList<WindowActivity> Windows { get; init; } = [];
    public IReadOnlyList<ApplicationIcon> ApplicationIcons { get; init; } = [];
    public IReadOnlyList<Guid> RemovedIntervalIds { get; init; } = [];
}

public interface IActivityStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RecordingBatch batch, CancellationToken cancellationToken = default);
    Task DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivitySpan>> ReadActivitySpansAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessActivity>> ReadProcessesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityInterval>> ReadIntervalsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityEvent>> ReadEventsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public interface IActivitySource : IAsyncDisposable
{
    Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default);
    Task StopAsync();
}
