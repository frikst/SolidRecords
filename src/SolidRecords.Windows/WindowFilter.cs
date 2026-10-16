using System.Text;

namespace SolidRecords.Windows;

internal sealed record WindowCandidate(string Application, string Title, string ClassName,
    uint ExtendedStyle, bool IsRoot, bool IsVisible, bool IsCloaked);

/// <summary>Admits document windows and dialogs, excluding shell surfaces and transient UI.</summary>
internal static class WindowFilter
{
    private const uint ToolWindow = 0x00000080;
    private const uint NoActivate = 0x08000000;
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "#32768", "tooltips_class32", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Progman", "WorkerW", "MultitaskingViewFrame", "TaskSwitcherWnd",
        "TaskListThumbnailWnd", "ThumbnailDeviceHelperWnd", "XamlExplorerHostIslandWindow",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"
    };
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "LockApp", "LogonUI", "winlogon", "dwm", "sihost", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost"
    };
    private static readonly HashSet<string> ExplorerDocumentClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CabinetWClass", "ExploreWClass", "#32770", "OperationStatusWindow"
    };

    public static bool IsUserWindow(WindowCandidate window)
    {
        if (!window.IsRoot || string.IsNullOrWhiteSpace(window.Title)) return false;
        if (ShellClasses.Contains(window.ClassName) || ShellProcesses.Contains(window.Application)) return false;
        if ((window.ExtendedStyle & (ToolWindow | NoActivate)) != 0) return false;
        // Explorer also owns the desktop, task switcher and many other shell surfaces.
        // Keep its folder windows and standard file-operation dialogs explicitly.
        return !window.Application.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
            ExplorerDocumentClasses.Contains(window.ClassName);
    }

    public static bool CanTrackForeground(WindowCandidate window) =>
        window.IsVisible && !window.IsCloaked && IsUserWindow(window);

    public static WindowCandidate Inspect(nint handle, string application)
    {
        var title = new StringBuilder(2048);
        var className = new StringBuilder(256);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        NativeMethods.GetClassName(handle, className, className.Capacity);
        var isCloaked = NativeMethods.DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        return new(application, title.ToString(), className.ToString(), NativeMethods.GetWindowStyle(handle, -20),
            NativeMethods.GetAncestor(handle, 2) == handle, NativeMethods.IsWindowVisible(handle), isCloaked);
    }
}
