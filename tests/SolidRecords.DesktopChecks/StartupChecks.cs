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
                    if (provider.GetRequiredService<PreferencesStore>().RecordOnLaunch != paused)
                        throw new InvalidOperationException("Recording preference was not saved.");
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
