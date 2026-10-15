using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Threading;
using SolidRecords.Core;

namespace SolidRecords.Windows;

internal sealed class ProcessMonitor : IDisposable
{
    private sealed record ProcessIdentity(string Name, long StartTicks);
    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    private readonly Action<CaptureSignal> _publish;
    private readonly Dispatcher _dispatcher;
    private ManagementEventWatcher? _start;
    private ManagementEventWatcher? _stop;
    private DispatcherTimer? _poll;
    private Dictionary<int, ProcessIdentity> _known = [];
    private bool _disposed;

    public ProcessMonitor(Action<CaptureSignal> publish, Dispatcher dispatcher)
    {
        _publish = publish;
        _dispatcher = dispatcher;
    }

    public string? Warning { get; private set; }

    public void Start()
    {
        try
        {
            _start = new(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _stop = new(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _start.EventArrived += OnStarted;
            _stop.EventArrived += OnStopped;
            _start.Start();
            _stop.Start();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            DisposeWatchers();
            var accessDenied = ex is UnauthorizedAccessException ||
                ex is ManagementException { ErrorCode: ManagementStatus.AccessDenied } ||
                ex.HResult is unchecked((int)0x80070005) or unchecked((int)0x80041003);
            var reason = accessDenied
                ? "Windows zamietol prístup k WMI udalostiam procesov."
                : $"Odber WMI udalostí procesov zlyhal: {ex.Message.Trim()} (0x{ex.HResult:X8}).";
            Warning = reason + " Procesy sa kontrolujú každú sekundu; krátko bežiace procesy môžu chýbať." +
                (accessDenied ? " Pre odber WMI skúste aplikáciu spustiť ako správca." : "");
            _poll = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                (_, _) => Poll(), _dispatcher);
        }
        _known = Snapshot();
        foreach (var (pid, process) in _known)
            _publish(new(SignalKind.ProcessObserved, DateTimeOffset.UtcNow, ProcessId: pid,
                Application: process.Name, Reason: "AlreadyRunning"));
    }

    private void OnStarted(object sender, EventArrivedEventArgs e) => Receive(e, SignalKind.ProcessStarted);
    private void OnStopped(object sender, EventArrivedEventArgs e) => Receive(e, SignalKind.ProcessStopped);

    private void Receive(EventArrivedEventArgs e, SignalKind kind)
    {
        using var data = e.NewEvent;
        if (Convert.ToInt32(data["SessionID"]) != _sessionId) return;
        var pid = Convert.ToInt32(data["ProcessID"]);
        var name = Convert.ToString(data["ProcessName"]) ?? "Unknown";
        var timestamp = DateTimeOffset.UtcNow;
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed) _publish(new(kind, timestamp, ProcessId: pid, Application: Path.GetFileNameWithoutExtension(name)));
        });
    }

    private void Poll()
    {
        var current = Snapshot();
        var now = DateTimeOffset.UtcNow;
        foreach (var (pid, previous) in _known)
            if (!current.TryGetValue(pid, out var next) || previous != next)
                _publish(new(SignalKind.ProcessStopped, now, ProcessId: pid, Application: previous.Name, Reason: "Polling"));
        foreach (var (pid, next) in current)
            if (!_known.TryGetValue(pid, out var previous) || previous != next)
                _publish(new(SignalKind.ProcessStarted, now, ProcessId: pid, Application: next.Name, Reason: "Polling"));
        _known = current;
    }

    private Dictionary<int, ProcessIdentity> Snapshot()
    {
        var result = new Dictionary<int, ProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == _sessionId)
                    {
                        long ticks;
                        try { ticks = process.StartTime.ToUniversalTime().Ticks; }
                        catch (System.ComponentModel.Win32Exception) { ticks = 0; }
                        result[process.Id] = new(process.ProcessName, ticks);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        return result;
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in new[] { _start, _stop })
        {
            if (watcher is null) continue;
            try { watcher.Stop(); }
            catch (ManagementException) { }
            watcher.Dispose();
        }
        _start = _stop = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _poll?.Stop();
        DisposeWatchers();
    }
}
