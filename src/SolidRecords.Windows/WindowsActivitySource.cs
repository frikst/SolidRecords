using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using SolidRecords.Core;

namespace SolidRecords.Windows;

/// <summary>Owns a native message loop independently of the visible application window.</summary>
public sealed class WindowsActivitySource : IActivitySource
{
    private readonly BrowserUrlReader _browser = new();
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
    private bool _active;
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
                _active = NativeMethods.IsDesktopActive();
                Emit(_active ? SignalKind.SessionActive : SignalKind.SessionInactive, "RecordingBaseline");
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
        SetSession(NativeMethods.IsDesktopActive(), "DesktopState");
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
        if (!NativeMethods.IsDesktopActive())
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
        _publish(new(SignalKind.ForegroundChanged, DateTimeOffset.UtcNow, next));
        if (next is not null) _windows?.ObserveForeground(next);
    }

    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x02B1) // WM_WTSSESSION_CHANGE; registered for this session only
        {
            switch ((int)wParam)
            {
                case 1: case 3: case 5: case 8:
                    SetSession(NativeMethods.IsDesktopActive(), $"WTS:{wParam}");
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
                SetSession(NativeMethods.IsDesktopActive(), "Resume");
                CaptureForeground();
            }
        }
        else if (message == 0x0011) // WM_QUERYENDSESSION: persist inactivity before logoff/shutdown
        {
            SetSession(false, "SessionEnding");
            handled = true;
            return 1;
        }
        return 0;
    }

    private void SetSession(bool active, string reason)
    {
        if (_active == active) return;
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
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _browser.Dispose();
        GC.KeepAlive(_callback);
    }
}
