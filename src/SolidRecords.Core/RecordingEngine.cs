namespace SolidRecords.Core;

/// <summary>Builds composites from window observations. Process lifetimes never contribute to tracked time.</summary>
public sealed class RecordingEngine
{
    private readonly record struct ProcessKey(int Pid, string Application, long? StartTicks);
    private readonly Dictionary<ProcessKey, ProcessActivity> _processes = [];
    private readonly Dictionary<(Guid Process, long Handle), WindowActivity> _windows = [];
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
        var processes = new List<ProcessActivity>();
        var windows = new List<WindowActivity>();
        var at = signal.Timestamp < _lastTime ? _lastTime : signal.Timestamp;
        _lastTime = at;
        RecordingBatch Batch() => new(events, intervals)
        {
            Processes = processes.GroupBy(x => x.Id).Select(group => group.Last()).ToArray(),
            Windows = windows.GroupBy(x => x.Id).Select(group => group.Last()).ToArray()
        };
        ProcessKey Key(WindowInfo info) => new(info.ProcessId, info.Application, info.ProcessStartTicks);
        void Emit(EventKind kind, WindowInfo? info = null, string? reason = null) => events.Add(new(
            Guid.NewGuid(), _recordingId, kind, at, info, info?.ProcessId, info?.Application, reason ?? signal.Reason));
        void SaveProcess(ProcessActivity process)
        {
            _processes[new(process.ProcessId, process.Application, process.ProcessStartTicks)] = process;
            processes.Add(process);
        }
        void SaveWindow(WindowActivity window)
        {
            _windows[(window.ProcessActivityId, window.Handle)] = window;
            windows.Add(window);
        }
        void CloseActivity(bool documentChanged = false)
        {
            if (_current is null) return;
            intervals.Add(_current with { End = at, IsOpen = false });
            if (_windows.TryGetValue((_current.ProcessActivityId, _current.Window.Handle), out var parentWindow))
                SaveWindow(parentWindow with { End = at });
            if (_processes.TryGetValue(Key(_current.Window), out var parentProcess))
                SaveProcess(parentProcess with { End = at });
            if (!documentChanged) Emit(EventKind.WindowDeactivated, _current.Window);
            _current = null;
        }
        WindowActivity Observe(WindowInfo info, bool allowNewProcess)
        {
            if (!_processes.TryGetValue(Key(info), out var process) || !process.IsOpen)
            {
                if (!allowNewProcess) throw new InvalidOperationException("An unobserved process cannot own a tracked window.");
                process = new(Guid.NewGuid(), _recordingId, info.ProcessId, info.Application,
                    info.ProcessStartTicks, at, at, true);
            }
            SaveProcess(process with { End = at });
            if (!_windows.TryGetValue((process.Id, info.Handle), out var window) || !window.IsOpen)
                window = new(Guid.NewGuid(), process.Id, info.Handle, info.Title, at, at, true);
            window = window with { Title = info.Title, End = at };
            SaveWindow(window);
            return window;
        }
        void ConfirmLifetimes()
        {
            foreach (var process in _processes.Values.Where(x => x.IsOpen).ToArray()) SaveProcess(process with { End = at });
            foreach (var window in _windows.Values.Where(x => x.IsOpen).ToArray()) SaveWindow(window with { End = at });
        }

        if (signal.Kind == SignalKind.RecordingStarted)
        {
            if (_recording) return Batch();
            _recordingId = Guid.NewGuid();
            _processes.Clear();
            _windows.Clear();
            _recording = true;
            _sessionActive = _sessionKnown = false;
            Emit(EventKind.RecordingStarted);
            return Batch();
        }
        if (!_recording) return Batch();

        switch (signal.Kind)
        {
            case SignalKind.RecordingStopped:
                CloseActivity();
                // The recording boundary closes observation, not the OS process itself.
                foreach (var process in _processes.Values.Where(x => x.IsOpen).ToArray())
                    SaveProcess(process with { End = at, IsOpen = false });
                foreach (var window in _windows.Values.Where(x => x.IsOpen).ToArray())
                    SaveWindow(window with { End = at, IsOpen = false });
                Emit(EventKind.RecordingStopped);
                _recording = false;
                _sessionActive = _sessionKnown = false;
                break;
            case SignalKind.SessionActive:
                if (!_sessionKnown || !_sessionActive) Emit(EventKind.SessionActive);
                _sessionActive = _sessionKnown = true;
                break;
            case SignalKind.SessionInactive:
                CloseActivity();
                if (!_sessionKnown || _sessionActive) Emit(EventKind.SessionInactive);
                _sessionActive = false;
                _sessionKnown = true;
                break;
            case SignalKind.ForegroundChanged when _sessionActive:
                if (_current?.Window == signal.Window) break;
                var sameWindow = _current is not null && signal.Window is not null &&
                    _current.Window.Handle == signal.Window.Handle && Key(_current.Window) == Key(signal.Window);
                // The address bar is read asynchronously after the window title. Its first
                // successful result enriches the current visit instead of splitting it.
                if (sameWindow && _current!.Window.Title == signal.Window!.Title &&
                    string.IsNullOrEmpty(_current.Window.Url) && !string.IsNullOrEmpty(signal.Window.Url))
                {
                    _current = _current with { Window = signal.Window, End = at };
                    Observe(signal.Window, false);
                    intervals.Add(_current);
                    Emit(EventKind.DocumentChanged, signal.Window, "UrlResolved");
                    break;
                }
                CloseActivity(sameWindow);
                if (signal.Window is not null)
                {
                    var window = Observe(signal.Window, true);
                    _current = new(Guid.NewGuid(), _recordingId, signal.Window, at, at, true,
                        window.ProcessActivityId, window.Id);
                    intervals.Add(_current);
                    Emit(sameWindow ? EventKind.DocumentChanged : EventKind.WindowActivated, signal.Window);
                }
                break;
            case SignalKind.WindowObserved when signal.Window is not null:
                if (_processes.TryGetValue(Key(signal.Window), out var owner) && owner.IsOpen)
                    Observe(signal.Window, false);
                break;
            case SignalKind.WindowClosed when signal.Window is not null:
                if (!_processes.TryGetValue(Key(signal.Window), out var parent) || !parent.IsOpen) break;
                if (!_windows.TryGetValue((parent.Id, signal.Window.Handle), out var closed) || !closed.IsOpen) break;
                if (_current?.WindowActivityId == closed.Id) CloseActivity();
                SaveWindow(closed with { End = at, IsOpen = false });
                if (!_windows.Values.Any(x => x.ProcessActivityId == parent.Id && x.IsOpen))
                {
                    SaveProcess(parent with { End = at, IsOpen = false });
                    Emit(EventKind.ProcessStopped, signal.Window, "LastWindowClosed");
                }
                break;
            case SignalKind.Heartbeat:
                ConfirmLifetimes();
                if (_current is not null)
                {
                    _current = _current with { End = at };
                    intervals.Add(_current);
                }
                Emit(EventKind.Heartbeat);
                break;
        }
        return Batch();
    }
}
