using System.Drawing;
using System.Windows.Forms;
using SolidRecords.App.Presentation;
using SolidRecords.Core.Recording;

namespace SolidRecords.App.Tray;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _applicationIcon;
    private readonly TrayMenu _menu;
    private readonly MainViewModel _viewModel;

    public TrayIcon(MainViewModel viewModel, Recorder recorder, Action showWindow, Action showAbout, Action showSettings, Func<Task> exit)
    {
        _viewModel = viewModel;
        _menu = TrayMenu.Create(showWindow, () => viewModel.ToggleRecordingCommand.Execute(null), showSettings, showAbout, exit);
        _menu.Opening += (_, _) => _menu.UpdateRecordingState(recorder.IsRecording, viewModel.ToggleRecordingCommand.CanExecute(null));
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
