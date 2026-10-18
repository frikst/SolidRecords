using System.IO.Pipes;
using System.Security.Principal;
using System.Windows.Threading;

namespace SolidRecords.App.Infrastructure;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private RegisteredWaitHandle? _registration;
    private Task? _listener;

    public SingleInstance()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var name = $"Global\\SolidRecords.{identity.User!.Value}{AppEnvironment.InstanceSuffix}";
        _pipeName = name.Replace('\\', '.');
        _mutex = new Mutex(true, name, out var firstInstance);
        IsFirstInstance = firstInstance;
        try { _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show"); }
        catch { _mutex.Dispose(); throw; }
    }

    public bool IsFirstInstance { get; }

    public void ShowExistingWindow() => _showEvent.Set();

    public void Listen(Dispatcher dispatcher, Action showWindow, Action<string, string>? updateAction = null)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(showWindow);
        }, null, Timeout.Infinite, false);
        _listener = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    // Fixed bound avoids letting an IPC client allocate an arbitrarily large string.
                    var buffer = new byte[256];
                    var count = 0;
                    while (count < buffer.Length)
                    {
                        var read = await pipe.ReadAsync(buffer.AsMemory(count), timeout.Token);
                        if (read == 0) break;
                        count += read;
                    }
                    var fields = System.Text.Encoding.UTF8.GetString(buffer, 0, count).Split('|');
                    if (count < buffer.Length && fields.Length == 2 && updateAction is not null && !dispatcher.HasShutdownStarted)
                        _ = dispatcher.BeginInvoke(() => updateAction(fields[0], fields[1]));
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            }
        });
    }

    public async Task SendUpdateActionAsync(string action, string tag)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await pipe.WriteAsync(System.Text.Encoding.UTF8.GetBytes(action + "|" + tag), timeout.Token);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _stop.Cancel();
        _listener?.GetAwaiter().GetResult();
        _stop.Dispose();
        _showEvent.Dispose();
        if (IsFirstInstance) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
