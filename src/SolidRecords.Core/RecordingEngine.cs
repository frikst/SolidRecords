namespace SolidRecords.Core;

/// <summary>Pure state machine: no Windows APIs, database calls or presentation state.</summary>
public sealed class RecordingEngine
{
    private Guid _recordingId;
    private bool _recording;
    private bool _sessionActive;
    private bool _sessionKnown;
    private ActivityInterval? _current;
    private DateTimeOffset _lastTime;

    public RecordingBatch Apply(CaptureSignal signal)
    {
        var events = new List<ActivityEvent>();
        var intervals = new List<ActivityInterval>();
        var at = signal.Timestamp < _lastTime ? _lastTime : signal.Timestamp;
        _lastTime = at;

        void Emit(EventKind kind, WindowInfo? window = null) => events.Add(new(
            Guid.NewGuid(), _recordingId, kind, at, window,
            window?.ProcessId ?? signal.ProcessId, window?.Application ?? signal.Application, signal.Reason));

        void CloseWindow()
        {
            if (_current is null) return;
            intervals.Add(_current with { End = at, IsOpen = false });
            Emit(EventKind.WindowDeactivated, _current.Window);
            _current = null;
        }

        if (signal.Kind == SignalKind.RecordingStarted)
        {
            if (_recording) return new(events, intervals);
            _recordingId = Guid.NewGuid();
            _recording = true;
            _sessionActive = false;
            _sessionKnown = false;
            Emit(EventKind.RecordingStarted);
            return new(events, intervals);
        }
        if (!_recording) return new(events, intervals);

        switch (signal.Kind)
        {
            case SignalKind.RecordingStopped:
                CloseWindow();
                Emit(EventKind.RecordingStopped);
                _recording = false;
                _sessionActive = false;
                _sessionKnown = false;
                break;
            case SignalKind.SessionActive:
                if (!_sessionKnown || !_sessionActive) Emit(EventKind.SessionActive);
                _sessionActive = true;
                _sessionKnown = true;
                break;
            case SignalKind.SessionInactive:
                CloseWindow();
                if (!_sessionKnown || _sessionActive) Emit(EventKind.SessionInactive);
                _sessionActive = false;
                _sessionKnown = true;
                break;
            case SignalKind.ForegroundChanged when _sessionActive:
                if (_current?.Window == signal.Window) break;
                var documentChanged = _current is not null && signal.Window is not null &&
                    _current.Window.Handle == signal.Window.Handle &&
                    _current.Window.ProcessId == signal.Window.ProcessId;
                if (documentChanged)
                {
                    intervals.Add(_current! with { End = at, IsOpen = false });
                    _current = null;
                    Emit(EventKind.DocumentChanged, signal.Window);
                }
                else CloseWindow();
                if (signal.Window is not null)
                {
                    _current = new(Guid.NewGuid(), _recordingId, signal.Window, at, at, true);
                    intervals.Add(_current);
                    if (!documentChanged) Emit(EventKind.WindowActivated, signal.Window);
                }
                break;
            case SignalKind.ProcessStarted:
                Emit(EventKind.ProcessStarted);
                break;
            case SignalKind.ProcessObserved:
                Emit(EventKind.ProcessObserved);
                break;
            case SignalKind.ProcessStopped:
                if (_current?.Window.ProcessId == signal.ProcessId) CloseWindow();
                Emit(EventKind.ProcessStopped);
                break;
            case SignalKind.Heartbeat:
                if (_current is not null)
                {
                    _current = _current with { End = at };
                    intervals.Add(_current);
                }
                Emit(EventKind.Heartbeat);
                break;
        }
        return new(events, intervals);
    }
}
