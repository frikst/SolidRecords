using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Interop;
using SolidRecords.App.Tray;

namespace SolidRecords.DesktopChecks;

internal static class TrayMenuPreview
{
    public static void Render(string directory, string theme)
    {
        using var menu = new TrayMenu();
        menu.Items.Add(new ToolStripMenuItem("Otvoriť SolidRecords") { Tag = TrayMenuSymbol.Window });
        var recording = new ToolStripMenuItem("Zastaviť záznam") { Tag = TrayMenuSymbol.Stop };
        menu.Items.Add(recording);
        var startup = new ToolStripMenuItem("Spúšťať po prihlásení") { Tag = TrayMenuSymbol.Startup, Checked = true };
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("O aplikácii") { Tag = TrayMenuSymbol.Info });
        menu.Items.Add(new ToolStripMenuItem("Ukončiť aplikáciu") { Tag = TrayMenuSymbol.Power });
        NativeMenuChecks.Run(menu, directory, theme);
        CheckInteraction(menu);
        // Open only this standalone menu: no NotifyIcon, registry changes or real App startup.
        menu.Show(new Point(40, 40));
        recording.Select();
        Save("recording");
        recording.Text = "Spustiť záznam";
        recording.Tag = TrayMenuSymbol.Play;
        startup.Checked = false;
        Save("stopped");
        Console.WriteLine($"Tray menu preview: {theme}, actual DPI {menu.DeviceDpi}; checked/unchecked and stop/play rendered.");
        menu.Close();

        void Save(string state)
        {
            using var bitmap = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
            bitmap.Save(Path.Combine(directory, $"tray-menu-{theme}-{state}.png"), ImageFormat.Png);
            var scale = 144f / menu.DeviceDpi;
            using var scaled = new Bitmap(bitmap, (int)(bitmap.Width * scale), (int)(bitmap.Height * scale));
            scaled.Save(Path.Combine(directory, $"tray-menu-{theme}-{state}-150.png"), ImageFormat.Png);
        }
    }
    private static void CheckInteraction(TrayMenu menu)
    {
        System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
        // Exercise the same internal positioning path used by NotifyIcon, including its first show.
        var showInTaskbar = typeof(ContextMenuStrip).GetMethod("ShowInTaskbar", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WinForms taskbar menu entry point was not found.");
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        var anchor = new Point(workArea.Right - 40, workArea.Bottom - 40);
        void Show() => showInTaskbar.Invoke(menu, [anchor.X, anchor.Y]);
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        void Key(Keys key)
        {
            var message = new MSG { hwnd = menu.Handle, message = 0x0100, wParam = (nint)key, lParam = 1 };
            ComponentDispatcher.RaiseThreadMessage(ref message);
        }
        Show();
        var firstBounds = menu.Bounds;
        Require(menu.Visible && firstBounds.Right <= anchor.X + 2 && firstBounds.Bottom <= anchor.Y + 2,
            $"First tray menu was not anchored above-left: {firstBounds}, anchor {anchor}.");
        menu.Items[0].Select();
        Key(Keys.Down);
        Require(menu.Items[1].Selected, "WPF did not forward arrow navigation to the WinForms menu.");
        Key(Keys.Escape);
        Require(!menu.Visible, "Escape did not close the tray menu through the WPF message filter.");
        Show();
        Require(menu.Bounds == firstBounds, $"Tray menu moved between first and second opening: {firstBounds} -> {menu.Bounds}.");
        var clicks = 0;
        EventHandler onClick = (_, _) => clicks++;
        menu.Items[0].Click += onClick;
        try
        {
            menu.Items[0].Select();
            Key(Keys.Enter);
            Require(clicks == 1 && !menu.Visible, "Enter did not invoke and close the selected menu action.");
        }
        finally { menu.Items[0].Click -= onClick; }
        Show();
        using var outside = new Control();
        var mouse = new MSG { hwnd = outside.Handle, message = 0x0201, wParam = 1, lParam = 0 };
        ComponentDispatcher.RaiseThreadMessage(ref mouse);
        Require(!menu.Visible, "Outside click did not dismiss the tray menu through the WPF message filter.");
        Console.WriteLine("Tray menu interaction: first/repeated anchor, arrows, Escape, Enter action and outside click passed.");
    }
}
