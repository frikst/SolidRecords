using System.Diagnostics;
using SolidRecords.Core.Updates;
using SolidRecords.Windows.Shell;
using SolidRecords.App.Infrastructure;

namespace SolidRecords.App.Updates;

internal sealed class UpdateController(UpdateChecker checker, UpdateInstaller installer,
    PreferencesStore preferences) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _actions = new(1, 1);
    private Task? _loop;
    private bool _installing;
    private Action<Exception>? _showError;

    public void Start(Action<Exception> showError)
    {
        _showError = showError;
        _loop ??= Task.Run(RunAsync);
    }

    public async Task RefreshAsync()
    {
        try { await checker.TickAsync(preferences.Current.CheckForUpdates, _stop.Token); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Trace.TraceWarning("Kontrola aktualizácií zlyhala: {0}", ex.Message); }
    }

    private async Task RunAsync()
    {
        await RefreshAsync();
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token)) await RefreshAsync();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task HandleActionAsync(string action, string tag, Func<Task> exit)
    {
        if (_stop.IsCancellationRequested || !_actions.Wait(0)) return;
        var shouldExit = false;
        string? downloadedPath = null;
        try
        {
            if (_installing) return;
            var release = await checker.ApplyActionAsync(action, tag, preferences.Current.CheckForUpdates, _stop.Token);
            if (release is null) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            var path = await installer.DownloadAsync(release, timeout.Token);
            downloadedPath = path;
            if (!preferences.Current.CheckForUpdates || _stop.IsCancellationRequested) return;
            // Obtain UAC consent while the app is still alive; the helper waits for this process to exit.
            using var helper = UpdateInstaller.LaunchAfterExit(path, release.Sha256);
            _installing = true;
            shouldExit = true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { _showError?.Invoke(ex); }
        finally
        {
            if (!shouldExit && downloadedPath is not null)
            {
                try { File.Delete(downloadedPath); Directory.Delete(Path.GetDirectoryName(downloadedPath)!); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.TraceWarning("Čistenie inštalátora zlyhalo: {0}", ex.Message); }
            }
            _actions.Release();
        }
        if (shouldExit) await exit();
    }

    public async Task StopAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null) await _loop;
        await _actions.WaitAsync();
        _actions.Release();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
    }
}
