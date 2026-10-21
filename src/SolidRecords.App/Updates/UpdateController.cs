using System.Diagnostics;
using SolidRecords.Core.Updates;
using SolidRecords.Windows.Shell;
using SolidRecords.App.Infrastructure;
using SolidRecords.Core.Localization;

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
        catch (Exception ex) { Trace.TraceWarning(Strings.Get("UpdateCheckFailed"), ex.Message); }
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
        try
        {
            await RunActionAsync(() => checker.ApplyActionAsync(action, tag, preferences.Current.CheckForUpdates, _stop.Token),
                exit, requireEnabled: true);
        }
        catch (Exception ex) { _showError?.Invoke(ex); }
    }

    public Task<string> UpdateNowAsync(Func<Task> exit) => RunActionAsync(async () =>
    {
        var result = await Task.Run(() => checker.CheckNowAsync(_stop.Token));
        if (result.RetryAt is { } retryAt)
            throw new InvalidOperationException(Strings.Format("RetryUpdate", retryAt.LocalDateTime));
        return result.Release;
    }, exit, requireEnabled: false);

    private async Task<string> RunActionAsync(Func<Task<UpdateRelease?>> getRelease, Func<Task> exit, bool requireEnabled)
    {
        if (_stop.IsCancellationRequested) return Strings.Get("AppExiting");
        if (!_actions.Wait(0)) return Strings.Get("UpdateInProgress");
        var shouldExit = false;
        string? downloadedPath = null;
        try
        {
            if (_installing) return Strings.Get("InstallInProgress");
            var release = await getRelease();
            if (release is null) return Strings.Get("NoUpdate");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            var path = await installer.DownloadAsync(release, timeout.Token);
            downloadedPath = path;
            if ((requireEnabled && !preferences.Current.CheckForUpdates) || _stop.IsCancellationRequested)
                return Strings.Get("UpdateCanceled");
            // Obtain UAC consent while the app is still alive; the helper waits for this process to exit.
            using var helper = UpdateInstaller.LaunchAfterExit(path, release.Sha256);
            _installing = true;
            shouldExit = true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return Strings.Get("UpdateCanceled"); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return Strings.Get("InstallCanceled"); }
        finally
        {
            if (!shouldExit && downloadedPath is not null)
            {
                try { File.Delete(downloadedPath); Directory.Delete(Path.GetDirectoryName(downloadedPath)!); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.TraceWarning(Strings.Get("InstallerCleanupFailed"), ex.Message); }
            }
            _actions.Release();
        }
        if (shouldExit) await exit();
        return Strings.Get("Installing");
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
