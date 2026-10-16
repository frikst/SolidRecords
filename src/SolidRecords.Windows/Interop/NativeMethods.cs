using System.Runtime.InteropServices;
using System.Text;

namespace SolidRecords.Windows.Interop;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint window, StringBuilder className, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern uint GetWindowStyle(nint window, int index);
    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);
    internal delegate void WinEventCallback(nint hook, uint kind, nint window, int objectId,
        int childId, uint threadId, uint eventTime);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback,
        uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSQuerySessionInformation(nint server, int sessionId, int infoClass,
        out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")]
    internal static extern void WTSFreeMemory(nint buffer);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SwitchDesktop(nint desktop);

    internal static bool IsDesktopActive()
    {
        // A disconnected RDP session may still have an accessible default desktop.
        if (WTSQuerySessionInformation(0, -1, 8, out var buffer, out var size)) // WTS_CURRENT_SESSION, WTSConnectState
        {
            try { if (size >= 4 && Marshal.ReadInt32(buffer) != 0) return false; }
            finally { WTSFreeMemory(buffer); }
        }
        var desktop = OpenInputDesktop(0, false, 0x0100); // DESKTOP_SWITCHDESKTOP
        if (desktop == 0) return false;
        try { return SwitchDesktop(desktop); }
        finally { CloseDesktop(desktop); }
    }
}
