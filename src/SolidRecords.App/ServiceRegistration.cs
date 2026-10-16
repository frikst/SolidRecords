using Microsoft.Extensions.DependencyInjection;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Storage;
using SolidRecords.Windows;

namespace SolidRecords.App;

internal static class ServiceRegistration
{
    public static IServiceCollection AddSolidRecords(this IServiceCollection services, string? dataDirectory = null)
    {
        dataDirectory ??= AppEnvironment.DataDirectory;
        services.AddSingleton(ActivityTrackingOptions.Default);
        services.AddSingleton(_ => new PreferencesStore(dataDirectory));
        services.AddSingleton<IActivityStore>(_ =>
            new SqliteActivityStore(Path.Combine(dataDirectory, "activity.db")));
        // Recorder owns and disposes its capture source.
        services.AddSingleton(provider => new Recorder(provider.GetRequiredService<IActivityStore>(),
            new WindowsActivitySource(), options: provider.GetRequiredService<ActivityTrackingOptions>()));
        services.AddSingleton(provider => new DailyReportService(provider.GetRequiredService<IActivityStore>(),
            options: provider.GetRequiredService<ActivityTrackingOptions>()));
        services.AddSingleton(provider => new MainViewModel(provider.GetRequiredService<Recorder>(),
            provider.GetRequiredService<DailyReportService>(), provider.GetRequiredService<PreferencesStore>().SaveAsync));
        services.AddSingleton<MainWindow>();
        services.AddSingleton<Func<MainWindow>>(provider => provider.GetRequiredService<MainWindow>);
        services.AddSingleton<TrayIconFactory>();
        services.AddSingleton<App>();
        return services;
    }
}
