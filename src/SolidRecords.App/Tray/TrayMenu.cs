using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SolidRecords.App.Tray;

internal enum TrayMenuSymbol { Window, Play, Stop, Settings, Info, Power }

internal sealed class TrayMenu : ContextMenuStrip
{
    private bool _nativeOpen;
    private NativeTrayMenu? _nativeHost;
    private readonly Font _menuFont = new("Segoe UI", 10f);

    public TrayMenu()
    {
        Font = _menuFont;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Renderer = new MenuRenderer();
    }

    internal static TrayMenu Create(Action showWindow, Action toggleRecording, Action showSettings, Action showAbout, Func<Task> exit)
    {
        var menu = new TrayMenu();
        menu.Items.Add(new ToolStripMenuItem("Otvoriť SolidRecords", null, (_, _) => showWindow()) { Tag = TrayMenuSymbol.Window });
        menu.Items.Add(new ToolStripMenuItem("Spustiť záznam", null, (_, _) => toggleRecording()) { Tag = TrayMenuSymbol.Play });
        menu.Items.Add(new ToolStripMenuItem("Nastavenia…", null, (_, _) => showSettings()) { Tag = TrayMenuSymbol.Settings });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("O aplikácii", null, (_, _) => showAbout()) { Tag = TrayMenuSymbol.Info });
        menu.Items.Add(new ToolStripMenuItem("Ukončiť aplikáciu", null, async (_, _) => await exit()) { Tag = TrayMenuSymbol.Power });
        return menu;
    }

    internal void UpdateRecordingState(bool isRecording, bool canToggle = true)
    {
        var toggle = Items[1];
        toggle.Text = isRecording ? "Zastaviť záznam" : "Spustiť záznam";
        toggle.Tag = isRecording ? TrayMenuSymbol.Stop : TrayMenuSymbol.Play;
        toggle.Enabled = canToggle;
    }

    protected override Padding DefaultPadding => new(Px(5));

    private int Px(float value) => (int)System.Math.Round(value * DeviceDpi / 96f);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = Px(248);
        var height = DefaultPadding.Vertical;
        foreach (ToolStripItem item in Items)
        {
            if (!item.Available) continue;
            if (item is not ToolStripSeparator)
                width = System.Math.Max(width, TextRenderer.MeasureText(item.Text, Font).Width + Px(72));
            height += Px(item is ToolStripSeparator ? 13 : 36);
        }
        return new Size(width + DefaultPadding.Horizontal, height);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        // Keep the native rows and the isolated renderer preview on the same metrics.
        var width = GetPreferredSize(Size.Empty).Width - DefaultPadding.Horizontal;
        SuspendLayout();
        try
        {
            foreach (ToolStripItem item in Items)
            {
                item.AutoSize = false;
                item.Size = new Size(width, Px(item is ToolStripSeparator ? 13 : 36));
                item.Margin = Padding.Empty;
            }
        }
        finally { ResumeLayout(false); }
        base.OnLayout(e);
    }

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        base.OnOpening(e);
        BackColor = ThemeColor("SolidBackgroundFillColorSecondaryBrush", SystemColors.Menu);
        ForeColor = ThemeColor("TextFillColorPrimaryBrush", SystemColors.MenuText);
        foreach (ToolStripItem item in Items) item.ForeColor = ForeColor;
    }

    internal void ShowNative(Point anchor)
    {
        if (_nativeOpen) return;
        _nativeOpen = true;
        var command = 0;
        try
        {
            var opening = new System.ComponentModel.CancelEventArgs();
            OnOpening(opening);
            if (opening.Cancel) return;
            PerformLayout();
            _nativeHost ??= new NativeTrayMenu(this);
            command = _nativeHost.Show(anchor);
        }
        finally { _nativeOpen = false; }
        // Run actions only after Windows has left the menu loop and native resources are released.
        if (command > 0 && command <= Items.Count) Items[command - 1].PerformClick();
    }

    internal void DrawNativeItem(Graphics graphics, int index, Rectangle bounds, bool selected)
    {
        var item = Items[index];
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(bounds.X, bounds.Y);
            using var brush = new SolidBrush(BackColor);
            graphics.FillRectangle(brush, 0, 0, bounds.Width, bounds.Height);
            var scale = DeviceDpi / 96f;
            if (item is ToolStripSeparator)
                MenuRenderer.DrawSeparator(graphics, bounds.Size, scale);
            else
            {
                if (selected && item.Enabled) MenuRenderer.DrawSelection(graphics, bounds.Size, scale);
                MenuRenderer.DrawContent(graphics, item, Font, bounds.Size, scale);
            }
        }
        finally { graphics.Restore(state); }
    }

    internal static Color ThemeColor(string key, Color fallback)
    {
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource(key) is not System.Windows.Media.SolidColorBrush brush) return fallback;
        var color = brush.Color;
        var background = app.TryFindResource("SolidBackgroundFillColorSecondaryBrush") as System.Windows.Media.SolidColorBrush;
        var backdrop = background?.Color ?? System.Windows.Media.Colors.White;
        return Color.FromArgb(
            (color.R * color.A + backdrop.R * (255 - color.A)) / 255,
            (color.G * color.A + backdrop.G * (255 - color.A)) / 255,
            (color.B * color.A + backdrop.B * (255 - color.A)) / 255);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _nativeHost?.Dispose();
        base.Dispose(disposing);
        if (disposing) _menuFont.Dispose();
    }

    private sealed class MenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(e.ToolStrip.BackColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(ThemeColor("CardStrokeColorDefaultSolidBrush", SystemColors.ControlDark));
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            DrawSelection(e.Graphics, e.Item.Size, scale);
        }

        internal static void DrawSelection(Graphics graphics, Size size, float scale)
        {
            using var brush = new SolidBrush(ThemeColor("SubtleFillColorSecondaryBrush", SystemColors.ControlLight));
            using var path = new GraphicsPath();
            var diameter = 8 * scale;
            var rect = new RectangleF(0, 0, size.Width - 1, size.Height - 1);
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            DrawContent(e.Graphics, e.Item, e.TextFont ?? e.Item.Font, e.Item.Size, scale);
        }

        internal static void DrawContent(Graphics graphics, ToolStripItem item, Font font, Size size, float scale)
        {
            var color = item.Enabled ? item.ForeColor : ThemeColor("TextFillColorDisabledBrush", SystemColors.GrayText);
            var bounds = new Rectangle((int)(42 * scale), 0, size.Width - (int)(54 * scale), size.Height);
            TextRenderer.DrawText(graphics, item.Text, font, bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
            if (item.Tag is not TrayMenuSymbol symbol) return;
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(12 * scale, (size.Height - 18 * scale) / 2);
            graphics.ScaleTransform(scale, scale);
            using var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            switch (symbol)
            {
                case TrayMenuSymbol.Window:
                    graphics.DrawRectangle(pen, 1.5f, 2.5f, 15, 13);
                    graphics.DrawLine(pen, 2, 6, 16, 6);
                    break;
                case TrayMenuSymbol.Play:
                    graphics.DrawPolygon(pen, [new PointF(5, 2.5f), new PointF(15, 9), new PointF(5, 15.5f)]);
                    break;
                case TrayMenuSymbol.Stop:
                    graphics.DrawRectangle(pen, 3, 3, 12, 12);
                    break;
                case TrayMenuSymbol.Settings:
                    graphics.DrawEllipse(pen, 4, 4, 10, 10);
                    graphics.DrawEllipse(pen, 7, 7, 4, 4);
                    for (var tooth = 0; tooth < 8; tooth++)
                    {
                        var angle = tooth * Math.PI / 4;
                        graphics.DrawLine(pen, 9 + (float)Math.Cos(angle) * 5, 9 + (float)Math.Sin(angle) * 5,
                            9 + (float)Math.Cos(angle) * 7.5f, 9 + (float)Math.Sin(angle) * 7.5f);
                    }
                    break;
                case TrayMenuSymbol.Info:
                    graphics.DrawEllipse(pen, 1.5f, 1.5f, 15, 15);
                    graphics.DrawLine(pen, 9, 8, 9, 12.5f);
                    graphics.DrawEllipse(pen, 8.65f, 4.7f, 0.7f, 0.7f);
                    break;
                case TrayMenuSymbol.Power:
                    graphics.DrawArc(pen, 2, 2, 14, 14, -50, 280);
                    graphics.DrawLine(pen, 9, 1, 9, 8);
                    break;
            }
            graphics.Restore(state);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            DrawSeparator(e.Graphics, e.Item.Size, (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
        }

        internal static void DrawSeparator(Graphics graphics, Size size, float scale)
        {
            var inset = (int)(10 * scale);
            using var pen = new Pen(ThemeColor("DividerStrokeColorDefaultBrush", SystemColors.ControlLight));
            graphics.DrawLine(pen, inset, size.Height / 2, size.Width - inset, size.Height / 2);
        }
    }
}
