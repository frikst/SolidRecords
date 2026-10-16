using System.Windows;
using SolidRecords.App.Infrastructure;
using SolidRecords.App.Presentation;
using SolidRecords.App.Tray;
using SolidRecords.Core;
using SolidRecords.Core.Recording;

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
    private SettingsWindow? _settingsWindow;
    private readonly ActivityTrackingOptions _trackingOptions;
    private readonly Action<bool> _setStartup;
    private readonly SemaphoreSlim _settingsLifecycle = new(1, 1);
    private bool _exiting;
    private bool _showRequested;

    public App(IActivityStore store, Recorder recorder, MainViewModel viewModel, Func<MainWindow> createWindow,
        PreferencesStore preferences, TrayIconFactory trayFactory, ActivityTrackingOptions trackingOptions, Action<bool> setStartup)
    {
        _store = store;
        _recorder = recorder;
        _viewModel = viewModel;
        _createWindow = createWindow;
        _preferences = preferences;
        _trayFactory = trayFactory;
        _trackingOptions = trackingOptions;
        _setStartup = setStartup;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await Task.Run(() => _store.InitializeAsync());
            await Task.Run(_preferences.LoadAsync);
            _trackingOptions.SetMinimumActivityDuration(TimeSpan.FromSeconds(_preferences.Current.MinimumActivitySeconds));
            ApplyTheme(_preferences.Current.Theme);
            _window = _createWindow();
            MainWindow = _window;
            _tray = _trayFactory.Create(ShowWindow, ShowAboutWindow, ShowSettingsWindow, ExitAsync);
            try { await Task.Run(() => _setStartup(_preferences.Current.StartWithWindows)); }
            catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            if (_preferences.RecordOnLaunch)
            {
                try { await _recorder.StartAsync(); }
                catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            }
            await _viewModel.RefreshAsync();
            if (_showRequested || !_preferences.Current.StartMinimized || e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowWindow();
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

    // WPF Fluent theme selection is the same preview API used by App.xaml.
#pragma warning disable WPF0001
    private void ApplyTheme(ApplicationTheme theme) => ThemeMode = theme switch
    {
        ApplicationTheme.Light => System.Windows.ThemeMode.Light,
        ApplicationTheme.Dark => System.Windows.ThemeMode.Dark,
        _ => System.Windows.ThemeMode.System
    };

#pragma warning restore WPF0001

    internal void ShowSettingsWindow()
    {
        if (_exiting) return;
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        var dialog = new SettingsWindow(new(_preferences.Current, SaveSettingsAsync));
        if (_window?.IsVisible == true && _window.WindowState != WindowState.Minimized) dialog.Owner = _window;
        else { dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen; dialog.ShowInTaskbar = true; }
        _settingsWindow = dialog;
        try { dialog.ShowDialog(); }
        finally { _settingsWindow = null; }
    }

    internal async Task SaveSettingsAsync(ApplicationSettings settings)
    {
        settings.Validate();
        await _settingsLifecycle.WaitAsync();
        try
        {
            if (_exiting) return;
            await ApplySettingsAsync(settings);
        }
        finally { _settingsLifecycle.Release(); }
    }

    private async Task ApplySettingsAsync(ApplicationSettings settings)
    {
        var previous = _preferences.Current;
        var thresholdChanged = settings.MinimumActivitySeconds != previous.MinimumActivitySeconds;
        var resume = thresholdChanged && _recorder.IsRecording;
        if (resume) await _recorder.StopAsync();
        try
        {
            await Task.Run(async () =>
            {
                if (settings.StartWithWindows != previous.StartWithWindows) _setStartup(settings.StartWithWindows);
                try { await _preferences.SaveAsync(settings); }
                catch
                {
                    if (settings.StartWithWindows != previous.StartWithWindows) _setStartup(previous.StartWithWindows);
                    throw;
                }
            });
            _trackingOptions.SetMinimumActivityDuration(TimeSpan.FromSeconds(settings.MinimumActivitySeconds));
            ApplyTheme(settings.Theme);
        }
        finally { if (resume && !_exiting) await _recorder.StartAsync(); }
        await _viewModel.RefreshAsync();
    }

    internal async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        await _settingsLifecycle.WaitAsync();
        _settingsLifecycle.Release();
        _aboutWindow?.Close();
        _settingsWindow?.Close();
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
