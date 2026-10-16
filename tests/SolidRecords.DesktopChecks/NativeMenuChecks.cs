using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidRecords.App;

namespace SolidRecords.DesktopChecks;

internal static class NativeMenuChecks
{
    public static void Run(TrayMenu menu, string directory, string theme)
    {
        var enteredMenuMode = false;
        var captured = false;
        var firstBounds = new Rect();
        Exception? error = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
                if (!GetGUIThreadInfo(0, ref info)) throw new InvalidOperationException("GetGUIThreadInfo failed.");
                enteredMenuMode = (info.Flags & 4) != 0 && info.MenuOwner != 0; // GUI_INMENUMODE
                var popup = FindWindow("#32768", null);
                if (popup != 0 && GetWindowRect(popup, out var bounds))
                {
                    GetWindowThreadProcessId(popup, out var processId);
                    if (processId != Environment.ProcessId) throw new InvalidOperationException("Unexpected menu owner process.");
                    firstBounds = bounds;
                    using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                    using (var graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
                    bitmap.Save(Path.Combine(directory, $"tray-native-{theme}.png"), ImageFormat.Png);
                    captured = true;
                }
            }
            catch (Exception ex) { error = ex; }
            finally { EndMenu(); }
        };
        var area = Screen.PrimaryScreen!.WorkingArea;
        timer.Start();
        menu.ShowNative(new Point(area.Right - 50, area.Bottom - 50));
        timer.Stop();
        if (error is not null) throw new InvalidOperationException("Native menu capture failed.", error);
        if (!enteredMenuMode || !captured) throw new InvalidOperationException("Popup did not enter the Windows native menu loop.");

        var clicks = 0;
        var actionOutsideLoop = false;
        var posted = false;
        var sameBounds = false;
        using var actionTimer = new System.Windows.Forms.Timer { Interval = 150 };
        EventHandler clicked = (_, _) =>
        {
            clicks++;
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            actionOutsideLoop = GetGUIThreadInfo(0, ref info) && (info.Flags & 4) == 0;
        };
        menu.Items[0].Click += clicked;
        actionTimer.Tick += (_, _) =>
        {
            if (posted) { actionTimer.Stop(); EndMenu(); return; }
            posted = true;
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(0, ref info)) { EndMenu(); return; }
            var popup = FindWindow("#32768", null);
            sameBounds = GetWindowRect(popup, out var bounds) && bounds.Equals(firstBounds);
            // Queue input only to our menu owner; do not inject global keyboard/mouse input.
            PostMessage(info.MenuOwner, 0x0100, (nuint)Keys.Down, 1);
            PostMessage(info.MenuOwner, 0x0100, (nuint)Keys.Enter, 1);
        };
        try
        {
            actionTimer.Start();
            menu.ShowNative(new Point(area.Right - 50, area.Bottom - 50));
        }
        finally { actionTimer.Stop(); menu.Items[0].Click -= clicked; }
        if (clicks != 1 || !actionOutsideLoop || !sameBounds)
            throw new InvalidOperationException($"Native action failed: clicks={clicks}, outsideLoop={actionOutsideLoop}, stableBounds={sameBounds}.");
        Console.WriteLine($"Native tray menu: {theme}, GUI_INMENUMODE observed; first/repeated bounds stable; cancellation and keyboard action passed after leaving menu mode.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretBounds;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndMenu();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? name);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect bounds);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
