using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SolidRecords.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            using var instance = new SingleInstance();
            if (!instance.IsFirstInstance)
            {
                instance.ShowExistingWindow();
                return 0;
            }

            var services = new ServiceCollection().AddSolidRecords();
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            try
            {
                var app = provider.GetRequiredService<App>();
                app.InitializeComponent();
                instance.Listen(app.Dispatcher, app.ShowWindow);
                return app.Run();
            }
            finally
            {
                // Run has returned: dispose UI subscriptions on their STA thread and drain the recorder.
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Aplikáciu sa nepodarilo spustiť alebo ukončiť:\n" + ex.Message,
                "SolidRecords", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
