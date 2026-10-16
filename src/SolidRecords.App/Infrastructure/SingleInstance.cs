using System.Security.Principal;
using System.Windows.Threading;

namespace SolidRecords.App.Infrastructure;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _registration;

    public SingleInstance()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var name = $"Global\\SolidRecords.{identity.User!.Value}{AppEnvironment.InstanceSuffix}";
        _mutex = new Mutex(true, name, out var firstInstance);
        IsFirstInstance = firstInstance;
        try { _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show"); }
        catch { _mutex.Dispose(); throw; }
    }

    public bool IsFirstInstance { get; }

    public void ShowExistingWindow() => _showEvent.Set();

    public void Listen(Dispatcher dispatcher, Action showWindow)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(showWindow);
        }, null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        _mutex.Dispose();
    }
}
