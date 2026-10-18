using System.Windows;
using System.Windows.Threading;
using SolidRecords.Windows.Shell;
using Microsoft.Extensions.DependencyInjection;
using SolidRecords.App.Infrastructure;
using SolidRecords.Core.Recording;

namespace SolidRecords.App;

internal static class Program
{
    private static int ForwardToastActivation(SingleInstance instance)
    {
        // COM normally activates the running server. If startup races with another instance,
        // keep pumping this STA until activation can be relayed to the owner of the recorder.
        using var notifications = new UpdateNotifications();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) => frame.Continue = false;
        notifications.SetActivationHandler((action, tag) =>
        {
            _ = Task.Run(async () =>
            {
                try { await instance.SendUpdateActionAsync(action, tag); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
                finally { _ = dispatcher.BeginInvoke(() => frame.Continue = false); }
            });
        });
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        return 0;
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            using var instance = new SingleInstance();
            if (!instance.IsFirstInstance)
            {
                if (UpdateNotifications.WasToastActivated) return ForwardToastActivation(instance);
                instance.ShowExistingWindow();
                return 0;
            }

            var services = new ServiceCollection().AddSolidRecords();
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            try
            {
                var app = provider.GetRequiredService<App>();
                app.InitializeComponent();
                instance.Listen(app.Dispatcher, app.ShowWindow, app.HandleUpdateActionAsync);
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
