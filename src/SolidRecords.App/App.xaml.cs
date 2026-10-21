using System.Windows;
using System.Windows.Threading;
using SolidRecords.App.Updates;
using SolidRecords.Core.Updates;
using SolidRecords.Windows.Shell;
using SolidRecords.App.Infrastructure;
using SolidRecords.App.Presentation;
using SolidRecords.App.Tray;
using SolidRecords.Core;
using SolidRecords.Core.Recording;
using SolidRecords.Core.Localization;

namespace SolidRecords.App;

internal partial class App : Application
{
    private readonly IActivityStore _store;
    private readonly Recorder _recorder;
    private readonly MainViewModel _viewModel;
    private readonly Func<MainWindow> _createWindow;
    private readonly PreferencesStore _preferences;
    private readonly TrayIconFactory _trayFactory;
    private GlobalShortcuts? _shortcuts;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private AboutWindow? _aboutWindow;
    private SettingsWindow? _settingsWindow;
    private readonly ActivityTrackingOptions _trackingOptions;
    private readonly Action<bool> _setStartup;
    private readonly SemaphoreSlim _settingsLifecycle = new(1, 1);
    private readonly UpdateController _updates;
    private readonly IUpdateNotifier _updateNotifier;
    private readonly RecordingAutomation _recordingAutomation = new();
    private DispatcherTimer? _scheduleTimer;
    private bool _scheduleBusy;
    private bool _shortcutBusy;
    private DateTimeOffset _nextRetentionCheck;
    private bool _exiting;
    private bool _showRequested;
    private bool _startupComplete;
    private readonly Queue<(string Action, string Tag)> _pendingUpdateActions = new();

    public App(IActivityStore store, Recorder recorder, MainViewModel viewModel, Func<MainWindow> createWindow,
        PreferencesStore preferences, TrayIconFactory trayFactory, ActivityTrackingOptions trackingOptions, Action<bool> setStartup, UpdateController updates, IUpdateNotifier updateNotifier)
    {
        _store = store;
        _recorder = recorder;
        _viewModel = viewModel;
        _createWindow = createWindow;
        _preferences = preferences;
        _trayFactory = trayFactory;
        _trackingOptions = trackingOptions;
        _setStartup = setStartup;
        _updates = updates;
        _updateNotifier = updateNotifier;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await Task.Run(() => _store.InitializeAsync());
            await Task.Run(_preferences.LoadAsync);
            LocalizationDictionary.Apply(_preferences.Current.Language);
            try { await ApplyRetentionAsync(force: true); }
            catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            _trackingOptions.SetMinimumActivityDuration(TimeSpan.FromSeconds(_preferences.Current.MinimumActivitySeconds));
            ApplyTheme(_preferences.Current.Theme);
            _window = _createWindow();
            MainWindow = _window;
            _shortcuts = new GlobalShortcuts();
            try { await _shortcuts.ApplyAsync(_preferences.Current, ToggleWindow, ToggleRecording, () => Task.CompletedTask); }
            catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            _tray = _trayFactory.Create(ShowWindow, ShowAboutWindow, ShowSettingsWindow, ExitAsync);
            try { await Task.Run(() => _setStartup(_preferences.Current.StartWithWindows)); }
            catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            _viewModel.ApplySettings(_preferences.Current);
            if (_recordingAutomation.Evaluate(_preferences.Current, DateTimeOffset.Now) == true)
            {
                try { await _recorder.StartAsync(); }
                catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
            }
            _scheduleTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
            _scheduleTimer.Tick += ScheduleTick;
            _scheduleTimer.Start();
            await _viewModel.RefreshAsync();
            _updates.Start(ex => Dispatcher.Invoke(() => MessageBox.Show(Strings.Get("UpdateFailedLine") + ex.Message,
                "SolidRecords", MessageBoxButton.OK, MessageBoxImage.Error)));
            _startupComplete = true;
            while (_pendingUpdateActions.TryDequeue(out var pending)) HandleUpdateActionAsync(pending.Action, pending.Tag);
            if (_updateNotifier is UpdateNotifications notifications)
                notifications.SetActivationHandler((action, tag) => Dispatcher.BeginInvoke(() => HandleUpdateActionAsync(action, tag)));
            if (_showRequested || !_preferences.Current.StartMinimized || e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.Get("StartupFailed") + ex.Message, "SolidRecords", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    private async void ScheduleTick(object? sender, EventArgs e)
    {
        if (_scheduleBusy || _exiting) return;
        _scheduleBusy = true;
        await _settingsLifecycle.WaitAsync();
        try
        {
            if (!_exiting)
            {
                await ApplyRecordingScheduleAsync();
                await ApplyRetentionAsync();
            }
        }
        catch (Exception ex) { _viewModel.ShowError(ex); }
        finally { _settingsLifecycle.Release(); _scheduleBusy = false; }
    }

    private async Task ApplyRetentionAsync(bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now < _nextRetentionCheck) return;
        // Back off on failure as well; report the error without retrying every second.
        _nextRetentionCheck = now.AddHours(1);
        if (ActivityRetention.GetCutoff(_preferences.Current, now) is { } cutoff)
            await _recorder.DeleteBeforeAsync(cutoff);
    }

    private async Task ApplyRecordingScheduleAsync()
    {
        var recording = _recordingAutomation.Evaluate(_preferences.Current, DateTimeOffset.Now);
        if (recording == true && !_recorder.IsRecording) await _recorder.StartAsync();
        else if (recording == false && _recorder.IsRecording) await _recorder.StopAsync();
    }

    internal async void HandleUpdateActionAsync(string action, string tag)
    {
        if (_exiting) return;
        if (!_startupComplete)
        {
            if (_pendingUpdateActions.Count < 16) _pendingUpdateActions.Enqueue((action, tag));
            return;
        }
        await _updates.HandleActionAsync(action, tag, ExitAsync);
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

    internal void ToggleWindow()
    {
        if (_exiting) return;
        // An owned modal dialog must remain reachable.
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        if (_aboutWindow is not null) { _aboutWindow.Activate(); return; }
        if (_window?.IsVisible == true && _window.WindowState != WindowState.Minimized) _window.Hide();
        else ShowWindow();
    }

    private async void ToggleRecording()
    {
        if (!_startupComplete || _exiting || _shortcutBusy || !_viewModel.ToggleRecordingCommand.CanExecute(null)) return;
        _shortcutBusy = true;
        await _settingsLifecycle.WaitAsync();
        try { if (!_exiting) await _viewModel.ToggleRecordingAsync(); }
        catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
        finally { _settingsLifecycle.Release(); _shortcutBusy = false; }
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
        var dialog = new SettingsWindow(new(_preferences.Current, SaveSettingsAsync, () => _updates.UpdateNowAsync(ExitAsync)));
        if (_window?.IsVisible == true && _window.WindowState != WindowState.Minimized) dialog.Owner = _window;
        else { dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen; dialog.ShowInTaskbar = true; }
        _settingsWindow = dialog;
        if (_shortcuts is not null) _shortcuts.Suspend();
        try { dialog.ShowDialog(); }
        finally
        {
            _settingsWindow = null;
            if (_shortcuts is not null && !_exiting)
            {
                _shortcuts.Resume();
                RestoreShortcuts();
            }
        }
    }

    private async void RestoreShortcuts()
    {
        if (_shortcuts is null || _exiting) return;
        try { await _shortcuts.ApplyAsync(_preferences.Current, ToggleWindow, ToggleRecording, () => Task.CompletedTask); }
        catch (Exception ex) { _viewModel.ShowError(ex); ShowWindow(); }
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
            async Task PersistAsync() => await Task.Run(async () =>
            {
                if (settings.StartWithWindows != previous.StartWithWindows) _setStartup(settings.StartWithWindows);
                try { await _preferences.SaveAsync(settings); }
                catch
                {
                    if (settings.StartWithWindows != previous.StartWithWindows) _setStartup(previous.StartWithWindows);
                    throw;
                }
            });
            if (_shortcuts is not null) await _shortcuts.ApplyAsync(settings, ToggleWindow, ToggleRecording, PersistAsync);
            else await PersistAsync();
            _trackingOptions.SetMinimumActivityDuration(TimeSpan.FromSeconds(settings.MinimumActivitySeconds));
            ApplyTheme(settings.Theme);
            LocalizationDictionary.Apply(settings.Language);
        }
        finally { if (resume && !_exiting) await _recorder.StartAsync(); }
        _viewModel.ApplySettings(settings);
        await ApplyRetentionAsync(force: true);
        await ApplyRecordingScheduleAsync();
        _ = Task.Run(_updates.RefreshAsync);
        await _viewModel.RefreshAsync();
    }

    internal async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        if (_scheduleTimer is not null)
        {
            _scheduleTimer.Stop();
            _scheduleTimer.Tick -= ScheduleTick;
        }
        await _updates.StopAsync();
        await _settingsLifecycle.WaitAsync();
        _settingsLifecycle.Release();
        _shortcuts?.Dispose();
        _aboutWindow?.Close();
        _settingsWindow?.Close();
        try { await _recorder.StopAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.Get("FlushFailed") + ex.Message, "SolidRecords", MessageBoxButton.OK, MessageBoxImage.Error);
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
        try { Task.Run(() => _recorder.StopForSessionEndAsync(e.ReasonSessionEnding == ReasonSessionEnding.Logoff)).Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        base.OnSessionEnding(e);
    }
}
