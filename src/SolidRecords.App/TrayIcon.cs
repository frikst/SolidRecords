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
    private readonly TrayMenu _menu;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _startup;
    private readonly MainViewModel _viewModel;

    public TrayIcon(MainViewModel viewModel, Recorder recorder, Action showWindow, Action showAbout, Func<Task> exit)
    {
        _viewModel = viewModel;
        _menu = new TrayMenu();
        _menu.Items.Add(new ToolStripMenuItem("Otvoriť SolidRecords", null, (_, _) => showWindow()) { Tag = TrayMenuSymbol.Window });
        _toggle = new ToolStripMenuItem("", null, (_, _) => viewModel.ToggleRecordingCommand.Execute(null));
        _menu.Items.Add(_toggle);
        _startup = new ToolStripMenuItem("Spúšťať po prihlásení") { CheckOnClick = false, Tag = TrayMenuSymbol.Startup };
        _startup.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled); }
            catch (Exception ex) { viewModel.ShowError(ex); showWindow(); }
        };
        _menu.Items.Add(_startup);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("O aplikácii", null, (_, _) => showAbout()) { Tag = TrayMenuSymbol.Info });
        _menu.Items.Add(new ToolStripMenuItem("Ukončiť aplikáciu", null, async (_, _) => await exit()) { Tag = TrayMenuSymbol.Power });
        _menu.Opening += (_, _) =>
        {
            _toggle.Text = recorder.IsRecording ? "Zastaviť záznam" : "Spustiť záznam";
            _toggle.Tag = recorder.IsRecording ? TrayMenuSymbol.Stop : TrayMenuSymbol.Play;
            _toggle.Enabled = viewModel.ToggleRecordingCommand.CanExecute(null);
            _startup.Checked = StartupRegistration.IsEnabled;
        };
        using (var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/SolidRecords.App;component/Assets/SolidRecords.ico")).Stream)
        {
            _applicationIcon = new Icon(resource, SystemInformation.SmallIconSize);
        }
        _icon = new NotifyIcon { Icon = _applicationIcon, Text = "SolidRecords", Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) _menu.ShowNative(Cursor.Position);
        };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) showWindow(); };
        _viewModel.PropertyChanged += ViewModelChanged;
        UpdateStatus();
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
