using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidRecords.App;
using SolidRecords.App.Infrastructure;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
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
                    var failingEditor = new SettingsViewModel(new(), _ => Task.FromException(new IOException("Test save failure")));
                    failingEditor.SaveCommand.Execute(null);
                    if (failingEditor.Error.Length == 0 || !failingEditor.IsEditable)
                        throw new InvalidOperationException("Settings editor did not recover from a save failure.");
                    var settings = new ApplicationSettings(0, false, false, false, ApplicationTheme.Dark);
                    var recordingBeforeSave = recorder.IsRecording;
                    await app.SaveSettingsAsync(settings);
                    if (recorder.IsRecording != recordingBeforeSave)
                        throw new InvalidOperationException("Saving settings changed the recording state.");
                    if (await provider.GetRequiredService<ISettingsStore>().ReadSettingsAsync() != settings
                        || provider.GetRequiredService<ActivityTrackingOptions>().MinimumActivityDuration != TimeSpan.Zero)
                        throw new InvalidOperationException("Settings did not persist or update shared thresholds.");
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
