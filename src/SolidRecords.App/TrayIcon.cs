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
