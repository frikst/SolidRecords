using System.ComponentModel;
using System.Text;
using SolidRecords.Core;

namespace SolidRecords.Windows;

/// <summary>
/// Enrolls a process only after a foreground visit. Thereafter it watches its top-level
/// windows, including minimized and previously observed hidden windows, until the last closes.
/// No process enumeration, WMI subscription or process-exit polling is involved.
/// </summary>
internal sealed class ObservedWindowRegistry(Action<CaptureSignal> publish)
{
    private readonly Dictionary<int, WindowInfo> _owners = [];
    private readonly Dictionary<long, WindowInfo> _windows = [];
    public bool Contains(long handle) => _windows.ContainsKey(handle);

    public void ObserveForeground(WindowInfo foreground)
    {
        _owners[foreground.ProcessId] = foreground with { Url = null };
        Refresh();
    }

    public void Refresh(long destroyedHandle = 0)
    {
        var current = new Dictionary<long, WindowInfo>();
        var success = NativeMethods.EnumWindows((handle, _) =>
        {
            if ((long)handle == destroyedHandle) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            if (!_owners.TryGetValue((int)pid, out var owner)) return true;
            if (!NativeMethods.IsWindowVisible(handle) && !_windows.ContainsKey(handle)) return true;
            var title = new StringBuilder(2048);
            NativeMethods.GetWindowText(handle, title, title.Capacity);
            current[handle] = owner with { Handle = handle, Title = title.ToString(), Url = null };
            return true;
        }, 0);
        if (!success) throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());

        var at = DateTimeOffset.UtcNow;
        // Register replacement windows before closing old windows, so a process that
        // opens a new window while closing another never temporarily loses all children.
        foreach (var (handle, info) in current)
            if (!_windows.TryGetValue(handle, out var previous) || previous != info)
                publish(new(SignalKind.WindowObserved, at, info));
        foreach (var (handle, previous) in _windows)
            if (!current.TryGetValue(handle, out var next) || previous.ProcessId != next.ProcessId ||
                previous.ProcessStartTicks != next.ProcessStartTicks)
                publish(new(SignalKind.WindowClosed, at, previous));
        _windows.Clear();
        foreach (var (handle, info) in current) _windows.Add(handle, info);
        foreach (var pid in _owners.Keys.Where(pid => !_windows.Values.Any(w => w.ProcessId == pid)).ToArray())
            _owners.Remove(pid);
    }
}
