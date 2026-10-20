using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using SolidRecords.Core;
using SolidRecords.Windows.Interop;

namespace SolidRecords.Windows.Capture;

/// <summary>Owns a native message loop independently of the visible application window.</summary>
public sealed class WindowsActivitySource : IActivitySource
{
    private readonly BrowserUrlReader _browser = new();
    private readonly ApplicationIconReader _icons = new();
    private readonly NativeMethods.WinEventCallback _callback;
    private Dispatcher? _dispatcher;
    private Thread? _thread;
    private HwndSource? _messageWindow;
    private DispatcherTimer? _timer;
    private ObservedWindowRegistry? _windows;
    private Action<CaptureSignal>? _publish;
    private nint _foregroundHook;
    private nint _nameHook;
    private nint _destroyHook;
    private WindowInfo? _foreground;
    private SessionCaptureState _sessionState = new();
    private bool _active;
    private bool _hasStarted;
    private bool _suspended;
    private long _generation;

    public WindowsActivitySource() => _callback = OnWinEvent;

    public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            Exception? startupFailure = null;
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _publish = publish;
                _windows = new ObservedWindowRegistry(publish);
                _generation++;
                _suspended = false;
                _sessionState = new();
                _messageWindow = new HwndSource(new HwndSourceParameters("SolidRecords capture")
                {
                    Width = 0, Height = 0, WindowStyle = 0x00800000 // hidden top-level window receives power broadcasts
                });
                _messageWindow.AddHook(OnMessage);
                if (!NativeMethods.WTSRegisterSessionNotification(_messageWindow.Handle, 0))
                    throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                _foregroundHook = NativeMethods.SetWinEventHook(3, 3, 0, _callback, 0, 0, 0);
                _nameHook = NativeMethods.SetWinEventHook(0x800C, 0x800C, 0, _callback, 0, 0, 0);
                _destroyHook = NativeMethods.SetWinEventHook(0x8001, 0x8001, 0, _callback, 0, 0, 0);
                if (_foregroundHook == 0 || _nameHook == 0 || _destroyHook == 0)
                    throw new Win32Exception("Nepodarilo sa zaregistrovať sledovanie okien.");
                PublishInitialSession(IsSessionActive());
                CaptureForeground();
                _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                    (_, _) => Tick(), _dispatcher);
                ready.SetResult();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                if (ready.Task.IsCompletedSuccessfully)
                    _publish?.Invoke(new(SignalKind.CaptureFailed, DateTimeOffset.UtcNow, Reason: ex.Message));
                else startupFailure = ex;
            }
            finally
            {
                Cleanup();
                if (_dispatcher is { HasShutdownStarted: false }) _dispatcher.InvokeShutdown();
                // Report startup failure only after the dispatcher has shut down;
                // the caller can then safely Stop a partially initialized source.
                if (startupFailure is not null) ready.TrySetException(startupFailure);
            }
        }) { IsBackground = true, Name = "Windows activity capture" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return ready.Task;
    }

    private void Tick()
    {
        if (_suspended) return;
        // Reconcile missed notifications, secure desktop and workstation state.
        SetSession(IsSessionActive(), "DesktopState");
        CaptureForeground();
        _windows?.Refresh();
        if (_active && _foreground is { } window && IsBrowser(window.Application))
        {
            var generation = _generation;
            var dispatcher = _dispatcher!;
            _browser.RequestUrl((nint)window.Handle, url =>
            {
                if (dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(() =>
                {
                    // A slow provider must never attach an old tab's URL to another window.
                    if (_publish is null || generation != _generation || !_active ||
                        _foreground != window || NativeMethods.GetForegroundWindow() != (nint)window.Handle) return;
                    var title = new StringBuilder(2048);
                    NativeMethods.GetWindowText((nint)window.Handle, title, title.Capacity);
                    if (title.ToString() != window.Title || url is null || url == window.Url) return;
                    _foreground = window with { Url = url };
                    _publish(new(SignalKind.ForegroundChanged, DateTimeOffset.UtcNow, _foreground));
                });
            });
        }
        Emit(SignalKind.Heartbeat);
    }

    private void OnWinEvent(nint hook, uint kind, nint window, int objectId, int childId, uint threadId, uint eventTime)
    {
        if (kind == 0x8001 && objectId == 0 && childId == 0)
        {
            if (_windows?.Contains(window) == true) _windows.Refresh(window);
            return;
        }
        if (kind == 3 || (objectId == 0 && window == NativeMethods.GetForegroundWindow())) CaptureForeground();
    }

    private void CaptureForeground()
    {
        if (!_active || _suspended || _publish is null) return;
        if (!IsSessionActive())
        {
            SetSession(false, "SecureDesktop");
            return;
        }
        var handle = NativeMethods.GetForegroundWindow();
        WindowInfo? next = null;
        if (handle != 0)
        {
            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var application = process.ProcessName;
                var candidate = WindowFilter.Inspect(handle, application);
                if (WindowFilter.CanTrackForeground(candidate))
                {
                    long? startTicks = null;
                    try { startTicks = process.StartTime.ToUniversalTime().Ticks; }
                    catch (Win32Exception) { }
                    next = new(handle, (int)pid, application, candidate.Title, ProcessStartTicks: startTicks);
                    if (_foreground is not null && next == _foreground with { Url = null })
                        next = next with { Url = _foreground.Url };
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
        }
        if (_foreground == next) return;
        _foreground = next;
        var icon = next is null ? null : _icons.Read(next);
        _publish(new(SignalKind.ForegroundChanged, DateTimeOffset.UtcNow, next, Icon: icon));
        if (next is not null) _windows?.ObserveForeground(next);
    }

    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x02B1) // WM_WTSSESSION_CHANGE; registered for this session only
        {
            _sessionState.ApplyWtsChange((int)wParam);
            switch ((int)wParam)
            {
                case 1: case 3: case 5: case 8:
                    SetSession(IsSessionActive(), $"WTS:{wParam}");
                    CaptureForeground();
                    break;
                case 2: case 4: case 6: case 7:
                    SetSession(false, $"WTS:{wParam}");
                    break;
            }
        }
        else if (message == 0x0218) // WM_POWERBROADCAST
        {
            if ((int)wParam == 4)
            {
                SetSession(false, "Suspend");
                _suspended = true;
            }
            else if ((int)wParam is 7 or 18)
            {
                _suspended = false;
                SetSession(IsSessionActive(), "Resume");
                CaptureForeground();
            }
        }
        else if (message == 0x0011) // WM_QUERYENDSESSION: persist inactivity before logoff/shutdown
        {
            _sessionState.IsEnding = true;
            SetSession(false, ((long)lParam & 0x80000000L) != 0 ? "Logoff" : "Shutdown");
            handled = true;
            return 1;
        }
        else if (message == 0x0016 && wParam == 0) // WM_ENDSESSION: shutdown/logoff was cancelled
        {
            _sessionState.IsEnding = false;
            SetSession(IsSessionActive(), "SessionEndingCancelled");
            CaptureForeground();
        }
        return 0;
    }

    internal void PublishInitialSession(bool active)
    {
        _active = active;
        Emit(active ? SignalKind.SessionActive : SignalKind.SessionInactive,
            active && !_hasStarted ? "ApplicationStartup" : "RecordingBaseline");
        _hasStarted = true;
    }

    private bool IsSessionActive() => _sessionState.CanCapture(NativeMethods.IsDesktopActive());

    private void SetSession(bool active, string reason)
    {
        if (_active == active)
        {
            // Logoff/shutdown is still a distinct event when the workstation is already locked.
            if (!active && reason is "Logoff" or "Shutdown") Emit(SignalKind.SessionInactive, reason);
            return;
        }
        _active = active;
        _foreground = null;
        Emit(active ? SignalKind.SessionActive : SignalKind.SessionInactive, reason);
    }

    private void Emit(SignalKind kind, string? reason = null) =>
        _publish?.Invoke(new(kind, DateTimeOffset.UtcNow, Reason: reason));

    private static bool IsBrowser(string name) => name.ToLowerInvariant() is
        "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi";

    public async Task StopAsync()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            await dispatcher.InvokeAsync(() =>
            {
                Cleanup();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            });
        if (_thread is not null) await Task.Run(() => _thread.Join());
        _thread = null;
        _dispatcher = null;
    }

    private void Cleanup()
    {
        _generation++;
        _timer?.Stop();
        _timer = null;
        if (_foregroundHook != 0) NativeMethods.UnhookWinEvent(_foregroundHook);
        if (_nameHook != 0) NativeMethods.UnhookWinEvent(_nameHook);
        if (_destroyHook != 0) NativeMethods.UnhookWinEvent(_destroyHook);
        _foregroundHook = _nameHook = _destroyHook = 0;
        if (_messageWindow is not null)
        {
            NativeMethods.WTSUnRegisterSessionNotification(_messageWindow.Handle);
            _messageWindow.Dispose();
            _messageWindow = null;
        }
        _publish = null;
        _foreground = null;
        _windows = null;
        _icons.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _browser.Dispose();
        GC.KeepAlive(_callback);
    }
}
