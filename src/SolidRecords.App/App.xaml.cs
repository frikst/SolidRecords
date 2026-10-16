using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Storage;
using SolidRecords.Windows;

namespace SolidRecords.App;

public partial class App : Application
{
    private sealed record Preferences(bool RecordOnLaunch = true);
    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showRegistration;
    private Recorder? _recorder;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private AboutWindow? _aboutWindow;
    private bool _exiting;
    private string _dataDirectory = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var instanceName = $"Global\\SolidRecords.{identity}{AppEnvironment.InstanceSuffix}";
        _instance = new Mutex(true, instanceName, out var firstInstance);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Show");
        if (!firstInstance) { _showEvent.Set(); Shutdown(); return; }
        try
        {
            _dataDirectory = AppEnvironment.DataDirectory;
            Directory.CreateDirectory(_dataDirectory);
            var store = new SqliteActivityStore(Path.Combine(_dataDirectory, "activity.db"));
            await Task.Run(() => store.InitializeAsync());
            var trackingOptions = ActivityTrackingOptions.Default;
            _recorder = new Recorder(store, new WindowsActivitySource(), options: trackingOptions);
            _viewModel = new MainViewModel(_recorder, new DailyReportService(store, options: trackingOptions), SavePreferenceAsync);
            _window = new MainWindow(_viewModel);
            MainWindow = _window;
            _tray = new TrayIcon(_viewModel, _recorder, ShowWindow, ShowAboutWindow, ExitAsync);
            _showRegistration = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, Timeout.Infinite, false);
            if (ReadPreferences().RecordOnLaunch)
            {
                try { await _recorder.StartAsync(); }
                catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            }
            await _viewModel.RefreshAsync();
            if (e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Aplikáciu sa nepodarilo spustiť:\n" + ex.Message, "SolidRecords", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private void ShowWindow()
    {
        if (_window is null || _exiting) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _viewModel?.RequestRefresh();
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

    private Preferences ReadPreferences()
    {
        try
        {
            var path = Path.Combine(_dataDirectory, "settings.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    private async Task SavePreferenceAsync(bool recordOnLaunch)
    {
        var path = Path.Combine(_dataDirectory, "settings.json");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Preferences(recordOnLaunch)));
        File.Move(temporary, path, true);
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _aboutWindow?.Close();
        _viewModel?.Dispose();
        try { if (_recorder is not null) await _recorder.DisposeAsync(); }
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
        if (_recorder is not null)
        {
            try { Task.Run(() => _recorder.StopAsync()).Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { }
        }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showRegistration?.Unregister(null);
        _showEvent?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
