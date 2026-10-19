using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidRecords.App;
using SolidRecords.App.Infrastructure;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Core.Updates;
using SolidRecords.Core.Recording;

namespace SolidRecords.DesktopChecks;

internal static class StartupChecks
{
    public static int Run(bool paused)
    {
        var directory = Path.GetFullPath(Path.Combine("artifacts", "startup-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), paused ? "{\"RecordOnLaunch\":false}" : "{\"RecordOnLaunch\":true}");
        var source = new EmptySource();
        var services = new ServiceCollection().AddSolidRecords(directory);
        services.RemoveAll<IUpdateNotifier>();
        services.AddSingleton<IUpdateNotifier, SilentNotifications>();
        services.RemoveAll<IReleaseClient>();
        services.AddSingleton<IReleaseClient, OfflineReleaseClient>();
        services.RemoveAll<Action<bool>>();
        services.AddSingleton<Action<bool>>(_ => _ => { });
        services.RemoveAll<Recorder>();
        services.AddSingleton(provider => new Recorder(provider.GetRequiredService<IActivityStore>(), source,
            options: provider.GetRequiredService<ActivityTrackingOptions>()));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Exception? failure = null;
        try
        {
            var app = provider.GetRequiredService<SolidRecords.App.App>();
            app.InitializeComponent();
            app.Startup += async (_, _) =>
            {
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (app.MainWindow is null || (!paused && !source.Started))
                    {
                        if (DateTime.UtcNow >= deadline) throw new TimeoutException("DI startup did not complete.");
                        await Task.Delay(20);
                    }

                    var viewModel = provider.GetRequiredService<MainViewModel>();
                    var recorder = provider.GetRequiredService<Recorder>();
                    if (!ReferenceEquals(app.MainWindow, provider.GetRequiredService<MainWindow>())
                        || !ReferenceEquals(app.MainWindow.DataContext, viewModel))
                        throw new InvalidOperationException("The window and view model are not shared DI instances.");
                    if (recorder.IsRecording == paused)
                        throw new InvalidOperationException("RecordOnLaunch was not respected.");
                    app.ShowWindow();
                    if (!app.MainWindow.IsVisible) throw new InvalidOperationException("Cannot show the DI window.");
                    app.MainWindow.Close();
                    if (app.MainWindow.IsVisible) throw new InvalidOperationException("Closing did not hide the window.");
                    await viewModel.ToggleRecordingAsync();
                    if (provider.GetRequiredService<PreferencesStore>().RecordOnLaunch == paused)
                        throw new InvalidOperationException("Recording toggle changed the launch preference.");
                    var saveCalls = 0;
                    var editor = new SettingsViewModel(new(), _ => { saveCalls++; return Task.CompletedTask; });
                    foreach (var invalid in new[] { "", "-1", "601", "1.5", "abc" })
                    {
                        editor.MinimumActivitySeconds = invalid;
                        editor.SaveCommand.Execute(null);
                        if (saveCalls != 0 || editor.Error.Length == 0)
                            throw new InvalidOperationException("Settings editor accepted an invalid threshold.");
                    }
                    editor.MinimumActivitySeconds = "600";
                    editor.SaveCommand.Execute(null);
                    if (saveCalls != 1 || editor.Error.Length != 0 || !editor.IsEditable)
                        throw new InvalidOperationException("Settings editor failed to save a valid threshold.");
                    ApplicationSettings? retentionSettings = null;
                    var retentionEditor = new SettingsViewModel(new(), saved => { retentionSettings = saved; return Task.CompletedTask; });
                    if (retentionEditor.DeleteOldActivities) throw new InvalidOperationException("Retention must default to never.");
                    retentionEditor.DeleteOldActivities = true;
                    foreach (var invalid in new[] { "", "0", "-1", "1.5", "abc", "2147483648" })
                    {
                        retentionEditor.ActivityRetentionDays = invalid;
                        retentionEditor.SaveCommand.Execute(null);
                        if (retentionSettings is not null || retentionEditor.Error.Length == 0)
                            throw new InvalidOperationException("Settings editor accepted invalid retention.");
                    }
                    retentionEditor.ActivityRetentionDays = "30";
                    retentionEditor.SaveCommand.Execute(null);
                    if (retentionSettings?.ActivityRetentionDays != 30) throw new InvalidOperationException("Retention days were not saved.");
                    retentionEditor.DeleteOldActivities = false;
                    retentionEditor.SaveCommand.Execute(null);
                    if (retentionSettings?.ActivityRetentionDays is not null) throw new InvalidOperationException("Never retention was not saved.");
                    ApplicationSettings? edited = null;
                    var workEditor = new SettingsViewModel(new(), saved => { edited = saved; return Task.CompletedTask; });
                    foreach (var (start, end) in new[] { ("24:00", "24:00"), ("15:00", "07:00"), ("07:00", "07:00"), ("bad", "15:00"), ("07:00", "25:00") })
                    {
                        workEditor.WorkStart = start;
                        workEditor.WorkEnd = end;
                        workEditor.SaveCommand.Execute(null);
                        if (edited is not null || workEditor.Error.Length == 0)
                            throw new InvalidOperationException("Settings editor accepted invalid work hours.");
                    }
                    workEditor.WorkStart = "8:30";
                    workEditor.WorkEnd = "24:00";
                    workEditor.RecordingModeIndex = 2;
                    workEditor.Monday = false;
                    workEditor.Sunday = true;
                    workEditor.SaveCommand.Execute(null);
                    if (edited is not { RecordOnLaunch: true, RecordOnlyDuringWorkHours: true, WorkStartMinutes: 510, WorkEndMinutes: 1440 }
                        || edited.WorkingDays != ((WorkDays.Weekdays & ~WorkDays.Monday) | WorkDays.Sunday))
                        throw new InvalidOperationException("Settings editor failed to save the work schedule.");
                    var failingEditor = new SettingsViewModel(new(), _ => Task.FromException(new IOException("Test save failure")));
                    failingEditor.SaveCommand.Execute(null);
                    if (failingEditor.Error.Length == 0 || !failingEditor.IsEditable)
                        throw new InvalidOperationException("Settings editor did not recover from a save failure.");
                    var updateCompletion = new TaskCompletionSource<string>();
                    var updateCalls = 0;
                    var updateEditor = new SettingsViewModel(new(CheckForUpdates: false), _ => Task.CompletedTask,
                        () => { updateCalls++; return updateCompletion.Task; });
                    updateEditor.UpdateNowCommand.Execute(null);
                    updateEditor.UpdateNowCommand.Execute(null);
                    if (updateCalls != 1 || updateEditor.IsEditable || !updateEditor.IsUpdating || updateEditor.UpdateStatus.Length == 0)
                        throw new InvalidOperationException("Manual update did not report progress or prevent duplicate execution.");
                    updateCompletion.SetResult("Test update complete");
                    await Task.Yield();
                    if (!updateEditor.IsEditable || updateEditor.CheckForUpdates || updateEditor.UpdateStatus != "Test update complete")
                        throw new InvalidOperationException("Manual update did not report completion or changed the daily preference.");
                    var failedUpdate = new SettingsViewModel(new(), _ => Task.CompletedTask,
                        () => Task.FromException<string>(new IOException("Test update failure")));
                    failedUpdate.UpdateNowCommand.Execute(null);
                    if (!failedUpdate.IsEditable || failedUpdate.IsUpdating || !failedUpdate.UpdateStatus.Contains("Test update failure", StringComparison.Ordinal))
                        throw new InvalidOperationException("Manual update did not recover from failure.");
                    await ShortcutChecks.RunAsync(app, provider);
                    var history = provider.GetRequiredService<IActivityStore>();
                    var oldAt = DateTimeOffset.UtcNow.AddDays(-60);
                    var oldId = Guid.NewGuid();
                    var recordingId = Guid.NewGuid();
                    await history.SaveAsync(new([], [new(oldId, recordingId, new(1, 2, "test", "old"), oldAt, oldAt.AddMinutes(1), false, oldId, oldId)])
                    {
                        Processes = [new(oldId, 2, "test", null, oldAt, oldAt.AddMinutes(1), false)],
                        Windows = [new(oldId, oldId, 1, "old", oldAt, oldAt.AddMinutes(1), false)]
                    });
                    await app.SaveSettingsAsync(provider.GetRequiredService<PreferencesStore>().Current);
                    if ((await history.ReadIntervalsAsync(oldAt, oldAt.AddHours(1))).Count != 1)
                        throw new InvalidOperationException("Never retention removed history.");
                    var settings = new ApplicationSettings(0, false, false, false, ApplicationTheme.Dark, false, ActivityRetentionDays: 30);
                    var recordingBeforeSave = recorder.IsRecording;
                    await app.SaveSettingsAsync(settings);
                    if ((await history.ReadIntervalsAsync(oldAt, oldAt.AddHours(1))).Count != 0)
                        throw new InvalidOperationException("Saving retention settings did not remove expired history.");
                    if (recorder.IsRecording != recordingBeforeSave)
                        throw new InvalidOperationException("Saving settings changed the recording state.");
                    if (await provider.GetRequiredService<ISettingsStore>().ReadSettingsAsync() != settings
                        || provider.GetRequiredService<ActivityTrackingOptions>().MinimumActivityDuration != TimeSpan.Zero)
                        throw new InvalidOperationException("Settings did not persist or update shared thresholds.");
                    var scheduled = settings with { RecordOnLaunch = true, RecordOnlyDuringWorkHours = true,
                        WorkStartMinutes = 0, WorkEndMinutes = 1440, WorkingDays = WorkDays.None };
                    await app.SaveSettingsAsync(scheduled);
                    if (recorder.IsRecording) throw new InvalidOperationException("An empty work week did not stop recording.");
                    scheduled = scheduled with { WorkingDays = WorkDays.All };
                    await app.SaveSettingsAsync(scheduled);
                    if (!recorder.IsRecording) throw new InvalidOperationException("The current work period did not start recording.");
                    var expectedBounds = Core.Reporting.TimelineRange.Calculate(DateOnly.FromDateTime(viewModel.SelectedDate!.Value), scheduled, viewModel.Timeline);
                    if (viewModel.TimelineBounds != expectedBounds) throw new InvalidOperationException("Timeline settings were not applied.");
                    await viewModel.ToggleRecordingAsync();
                    await Task.Delay(1200);
                    if (recorder.IsRecording) throw new InvalidOperationException("The schedule undid a manual pause between boundaries.");
                    await app.SaveSettingsAsync(settings);
                    // A later load must prefer the database over the original JSON file.
                    await provider.GetRequiredService<PreferencesStore>().LoadAsync();
                    if (provider.GetRequiredService<PreferencesStore>().Current != settings)
                        throw new InvalidOperationException("Legacy JSON overwrote database settings.");
                }
                catch (Exception ex) { failure = ex; }
                finally { await app.ExitAsync(); }
            };
            app.Run();
        }
        finally
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (failure is not null) throw new InvalidOperationException("DI startup check failed.", failure);
        if (source.DisposalCount != 1) throw new InvalidOperationException("Capture source was not disposed exactly once.");
        Console.WriteLine($"PASS: DI startup ({(paused ? "paused" : "recording")}), shared services, show/hide, preferences and shutdown.");
        return 0;
    }

    private sealed class SilentNotifications : IUpdateNotifier
    {
        public void Show(UpdateRelease release) { }
        public void Clear() { }
    }

    private sealed class OfflineReleaseClient : IReleaseClient
    {
        public Task<ReleaseCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken) =>
            Task.FromResult(new ReleaseCheckResult(false));
    }

    private sealed class EmptySource : IActivitySource
    {
        public bool Started { get; private set; }
        public int DisposalCount { get; private set; }

        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            DisposalCount++;
            return ValueTask.CompletedTask;
        }
    }
}
