using System.Drawing;
using System.Windows.Forms;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Windows;

namespace SolidRecords.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _applicationIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _startup;
    private readonly MainViewModel _viewModel;

    public TrayIcon(MainViewModel viewModel, Recorder recorder, Action showWindow, Func<Task> exit)
    {
        _viewModel = viewModel;
        _menu = new ContextMenuStrip();
        _menu.Items.Add("Otvoriť SolidRecords", null, (_, _) => showWindow());
        _toggle = new ToolStripMenuItem("", null, (_, _) => viewModel.ToggleRecordingCommand.Execute(null));
        _menu.Items.Add(_toggle);
        _menu.Items.Add(new ToolStripSeparator());
        _startup = new ToolStripMenuItem("Spúšťať po prihlásení") { CheckOnClick = false };
        _startup.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled); }
            catch (Exception ex) { viewModel.ShowError(ex); showWindow(); }
        };
        _menu.Items.Add(_startup);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Ukončiť aplikáciu", null, async (_, _) => await exit());
        _menu.Opening += (_, _) =>
        {
            // Read on every opening, including after a theme change while the window is hidden.
            var colors = new ThemeMenuColors();
            _menu.Renderer = new ToolStripProfessionalRenderer(colors);
            _menu.ForeColor = colors.Foreground;
            foreach (ToolStripItem item in _menu.Items) item.ForeColor = colors.Foreground;
            _toggle.Text = recorder.IsRecording ? "Zastaviť záznam" : "Spustiť záznam";
            _toggle.Enabled = viewModel.ToggleRecordingCommand.CanExecute(null);
            _startup.Checked = StartupRegistration.IsEnabled;
        };
        using (var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/SolidRecords.App;component/Assets/SolidRecords.ico")).Stream)
        {
            _applicationIcon = new Icon(resource, SystemInformation.SmallIconSize);
        }
        _icon = new NotifyIcon { Icon = _applicationIcon, Text = "SolidRecords", ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => showWindow();
        _viewModel.PropertyChanged += ViewModelChanged;
        UpdateStatus();
    }

    private sealed class ThemeMenuColors : ProfessionalColorTable
    {
        private readonly Color _background = ThemeColor("SolidBackgroundFillColorSecondaryBrush", SystemColors.Menu);
        private readonly Color _border = ThemeColor("CardStrokeColorDefaultSolidBrush", SystemColors.ControlDark);
        private readonly Color _selected = ThemeColor("ControlSolidFillColorDefaultBrush", SystemColors.ControlLight);
        public Color Foreground { get; } = ThemeColor("TextFillColorPrimaryBrush", SystemColors.MenuText);

        public ThemeMenuColors() => UseSystemColors = false;

        public override Color ToolStripDropDownBackground => _background;
        public override Color ImageMarginGradientBegin => _background;
        public override Color ImageMarginGradientMiddle => _background;
        public override Color ImageMarginGradientEnd => _background;
        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => _border;
        public override Color MenuItemSelected => _selected;
        public override Color MenuItemSelectedGradientBegin => _selected;
        public override Color MenuItemSelectedGradientEnd => _selected;
        public override Color CheckBackground => _selected;
        public override Color CheckSelectedBackground => _selected;
        public override Color CheckPressedBackground => _selected;
        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _background;

        private static Color ThemeColor(string key, Color fallback)
        {
            if (System.Windows.Application.Current.TryFindResource(key) is not System.Windows.Media.SolidColorBrush brush)
                return fallback;
            // Fluent text brushes use alpha; WinForms menu text needs the equivalent opaque color.
            var color = brush.Color;
            var background = System.Windows.Application.Current.TryFindResource("SolidBackgroundFillColorSecondaryBrush")
                as System.Windows.Media.SolidColorBrush;
            var backdrop = background?.Color ?? System.Windows.Media.Colors.White;
            return Color.FromArgb(
                (color.R * color.A + backdrop.R * (255 - color.A)) / 255,
                (color.G * color.A + backdrop.G * (255 - color.A)) / 255,
                (color.B * color.A + backdrop.B * (255 - color.A)) / 255);
        }
    }

    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Status)) UpdateStatus();
    }
    private void UpdateStatus() => _icon.Text = "SolidRecords · " + _viewModel.Status;

    public void Dispose()
    {
        _viewModel.PropertyChanged -= ViewModelChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _applicationIcon.Dispose();
        _menu.Dispose();
    }
}
