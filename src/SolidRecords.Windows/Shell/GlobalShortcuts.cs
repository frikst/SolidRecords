using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SolidRecords.Core;
using SolidRecords.Core.Localization;

namespace SolidRecords.Windows.Shell;

/// <summary>Owns a message window independently of the application's visible windows. Use on its dispatcher.</summary>
public sealed class GlobalShortcuts : IDisposable
{
    private readonly HwndSource _source = new(new HwndSourceParameters("SolidRecords shortcuts") { ParentWindow = new nint(-3) });
    private readonly Dictionary<GlobalShortcut, int> _registrations = [];
    private Dictionary<int, Action> _actions = [];
    private int _nextId;
    private bool _updating;
    private bool _disposed;
    public bool IsSuspended { get; private set; }

    public void Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsSuspended = true;
        foreach (var shortcut in _registrations.Keys.ToArray()) Remove(shortcut);
        _actions.Clear();
    }

    public void Resume() => IsSuspended = false;

    public GlobalShortcuts() => _source.AddHook(WindowMessage);

    public async Task ApplyAsync(ApplicationSettings settings, Action toggleWindow, Action toggleRecording, Func<Task> save)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.Validate();
        if (_updating) throw new InvalidOperationException(Strings.Get("ShortcutsBusy"));
        _updating = true;
        var added = new List<GlobalShortcut>();
        var desired = new Dictionary<GlobalShortcut, Action>();
        if (settings.ToggleWindowShortcut is { } window) desired.Add(window, toggleWindow);
        if (settings.ToggleRecordingShortcut is { } recording) desired.Add(recording, toggleRecording);
        try
        {
            // Keep existing registrations until persistence succeeds; reuse them when actions swap.
            foreach (var shortcut in desired.Keys)
            {
                if (_registrations.ContainsKey(shortcut)) continue;
                var id = ++_nextId;
                if (!RegisterHotKey(_source.Handle, id, (uint)shortcut.Modifiers | 0x4000, (uint)shortcut.VirtualKey))
                    throw new InvalidOperationException(Strings.Get("ShortcutConflict"),
                        new Win32Exception(Marshal.GetLastWin32Error()));
                _registrations.Add(shortcut, id);
                added.Add(shortcut);
            }
            await save();
            foreach (var shortcut in _registrations.Keys.Except(desired.Keys).ToArray()) Remove(shortcut);
            _actions = desired.ToDictionary(pair => _registrations[pair.Key], pair => pair.Value);
        }
        catch
        {
            foreach (var shortcut in added) Remove(shortcut);
            throw;
        }
        finally
        {
            // Even after a failed save the editor must be able to capture any combination.
            if (IsSuspended) Suspend();
            _updating = false;
        }
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && !_updating && !IsSuspended && _actions.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            action();
        }
        return 0;
    }

    private void Remove(GlobalShortcut shortcut)
    {
        UnregisterHotKey(_source.Handle, _registrations[shortcut]);
        _registrations.Remove(shortcut);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var shortcut in _registrations.Keys.ToArray()) Remove(shortcut);
        _actions.Clear();
        _source.RemoveHook(WindowMessage);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
