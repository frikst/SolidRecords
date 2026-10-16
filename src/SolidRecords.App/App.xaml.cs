using System.Windows;
using SolidRecords.App.Presentation;
using SolidRecords.Core;

namespace SolidRecords.App;

internal partial class App : Application
{
    private readonly IActivityStore _store;
    private readonly Recorder _recorder;
    private readonly MainViewModel _viewModel;
    private readonly Func<MainWindow> _createWindow;
    private readonly PreferencesStore _preferences;
    private readonly TrayIconFactory _trayFactory;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private AboutWindow? _aboutWindow;
    private bool _exiting;
    private bool _showRequested;

    public App(IActivityStore store, Recorder recorder, MainViewModel viewModel, Func<MainWindow> createWindow,
        PreferencesStore preferences, TrayIconFactory trayFactory)
    {
        _store = store;
        _recorder = recorder;
        _viewModel = viewModel;
        _createWindow = createWindow;
        _preferences = preferences;
        _trayFactory = trayFactory;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await Task.Run(() => _store.InitializeAsync());
            _window = _createWindow();
            MainWindow = _window;
            _tray = _trayFactory.Create(ShowWindow, ShowAboutWindow, ExitAsync);
            if (_preferences.RecordOnLaunch)
            {
                try { await _recorder.StartAsync(); }
                catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            }
            await _viewModel.RefreshAsync();
            if (_showRequested || e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Aplikáciu sa nepodarilo spustiť:\n" + ex.Message, "SolidRecords", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    internal void ShowWindow()
    {
        if (_exiting) return;
        if (_window is null) { _showRequested = true; return; }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _viewModel.RequestRefresh();
    }

    private void ShowAboutWindow()
    {
        if (_exiting) return;
        if (_aboutWindow is not null) { _aboutWindow.Activate(); return; }
        var dialog = new AboutWindow();
        if (_window?.IsVisible == true && _window.WindowState != WindowState.Minimized)
        {
            dialog.Owner = _window;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
        }
        _aboutWindow = dialog;
        try { dialog.ShowDialog(); }
        finally { _aboutWindow = null; }
    }

    internal async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _aboutWindow?.Close();
        try { await _recorder.StopAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show("Zápis posledných udalostí zlyhal:\n" + ex.Message, "SolidRecords", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _tray?.Dispose();
            if (_window is not null) { _window.AllowClose = true; _window.Close(); }
            Shutdown();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // The capture source has its own dispatcher, so flushing from a worker does
        // not depend on the UI thread that Windows is currently shutting down.
        try { Task.Run(() => _recorder.StopAsync()).Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        base.OnSessionEnding(e);
    }
}
