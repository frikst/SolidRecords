using Microsoft.Extensions.DependencyInjection;
using SolidRecords.App.Infrastructure;
using SolidRecords.App.Presentation;
using SolidRecords.App.Tray;
using SolidRecords.App.Updates;
using SolidRecords.Core.Updates;
using SolidRecords.Windows.Shell;
using System.Net.Http;
using SolidRecords.Core;
using SolidRecords.Core.Recording;
using SolidRecords.Core.Reporting;
using SolidRecords.Storage;
using SolidRecords.Windows.Capture;

namespace SolidRecords.App;

internal static class ServiceRegistration
{
    public static IServiceCollection AddSolidRecords(this IServiceCollection services, string? dataDirectory = null)
    {
        dataDirectory ??= AppEnvironment.DataDirectory;
        services.AddSingleton(new ActivityTrackingOptions());
        services.AddSingleton(provider => new PreferencesStore(provider.GetRequiredService<ISettingsStore>(), dataDirectory));
        services.AddSingleton<ISettingsStore>(provider => (ISettingsStore)provider.GetRequiredService<IActivityStore>());
        services.AddSingleton<Action<bool>>(_ => SolidRecords.Windows.Shell.StartupRegistration.SetEnabled);
        services.AddSingleton<IActivityStore>(_ =>
            new SqliteActivityStore(Path.Combine(dataDirectory, "activity.db")));
        // Recorder owns and disposes its capture source.
        services.AddSingleton(provider => new Recorder(provider.GetRequiredService<IActivityStore>(),
            new WindowsActivitySource(), options: provider.GetRequiredService<ActivityTrackingOptions>()));
        services.AddSingleton(provider => new DailyReportService(provider.GetRequiredService<IActivityStore>(),
            options: provider.GetRequiredService<ActivityTrackingOptions>()));
        services.AddSingleton(provider => new MainViewModel(provider.GetRequiredService<Recorder>(),
            provider.GetRequiredService<DailyReportService>()));
        services.AddSingleton<MainWindow>();
        services.AddSingleton<Func<MainWindow>>(provider => provider.GetRequiredService<MainWindow>);
        services.AddSingleton<TrayIconFactory>();
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(15) });
        services.AddSingleton<IReleaseClient, GitHubReleaseClient>();
        services.AddSingleton<IUpdateStateStore>(_ => new UpdateStateStore(dataDirectory));
        services.AddSingleton<IUpdateNotifier, UpdateNotifications>();
        services.AddSingleton(provider => new UpdateChecker(provider.GetRequiredService<IReleaseClient>(),
            provider.GetRequiredService<IUpdateStateStore>(), provider.GetRequiredService<IUpdateNotifier>(),
            typeof(App).Assembly.GetName().Version!));
        services.AddSingleton(provider => new UpdateInstaller(provider.GetRequiredService<HttpClient>(), dataDirectory));
        services.AddSingleton<UpdateController>();
        services.AddSingleton<App>();
        return services;
    }
}
