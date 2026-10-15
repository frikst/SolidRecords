using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Storage;
using SolidRecords.Windows;

namespace SolidRecords.DesktopChecks;

/// <summary>Opt-in Windows integration checks. Uses isolated databases, never the user's history.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var directory = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/desktop-checks");
            Directory.CreateDirectory(directory);
            CheckCaptureAsync(directory).GetAwaiter().GetResult();
            RenderWindow(directory);
            Console.WriteLine("PASS: Windows capture, process lifecycle, SQLite persistence and WPF rendering.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task CheckCaptureAsync(string directory)
    {
        var store = new SqliteActivityStore(Path.Combine(directory, $"capture-{Guid.NewGuid():N}.db"));
        var source = new WindowsActivitySource();
        await using var recorder = new Recorder(store, source);
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await recorder.StartAsync();
        // A child process that lives long enough to be seen by the polling fallback, too.
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping -n 4 127.0.0.1 >nul")
        { UseShellExecute = false, CreateNoWindow = true })!;
        var pid = process.Id;
        await process.WaitForExitAsync();
        await Task.Delay(2200);
        await recorder.StopAsync();
        var events = await store.ReadEventsAsync(start, DateTimeOffset.UtcNow.AddSeconds(1));
        Require(events.Any(x => x.Kind == EventKind.RecordingStarted), "Missing recording start");
        Require(events.Any(x => x.Kind == EventKind.SessionActive || x.Kind == EventKind.SessionInactive), "Missing session baseline");
        Require(events.Any(x => x.Kind == EventKind.ProcessStarted && x.ProcessId == pid), "Missing child process start");
        Require(events.Any(x => x.Kind == EventKind.ProcessStopped && x.ProcessId == pid), "Missing child process exit");
        Require(events.Last().Kind == EventKind.RecordingStopped, "Recording did not drain on stop");
        Console.WriteLine($"Capture: {events.Count} events. {source.Warning ?? "WMI process events enabled."}");
        // Verify lifecycle restart releases native hooks and the message window correctly.
        await recorder.StartAsync();
        await Task.Delay(300);
        await recorder.StopAsync();
    }

    private static void RenderWindow(string directory)
    {
        var bindings = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindings));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var app = new SolidRecords.App.App();
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var store = new SqliteActivityStore(Path.Combine(directory, $"preview-{Guid.NewGuid():N}.db"));
        var day = new DateTimeOffset(DateTime.Today.AddHours(8));
        var recording = Guid.NewGuid();
        var samples = new[]
        {
            ("msedge", "Projekt SolidRecords · dokumentácia", "https://learn.microsoft.com/dotnet/", 0, 38),
            ("devenv", "SolidRecords – Microsoft Visual Studio", (string?)null, 38, 74),
            ("WindowsTerminal", "PowerShell · dotnet test", (string?)null, 112, 12),
            ("msedge", "WPF · UI Automation", "https://learn.microsoft.com/dotnet/desktop/wpf/", 124, 26),
            ("devenv", "MainWindow.xaml – SolidRecords", (string?)null, 165, 55),
            ("notepad", "Poznámky k projektu.txt", (string?)null, 220, 8)
        };
        var intervals = samples.Select((x, i) => new ActivityInterval(Guid.NewGuid(), recording,
            new(i + 1, i + 20, x.Item1, x.Item2, x.Item3), day.AddMinutes(x.Item4), day.AddMinutes(x.Item4 + x.Item5), false)).ToArray();
        store.SaveAsync(new([], intervals)).GetAwaiter().GetResult();
        var recorder = new Recorder(store, new EmptySource());
        var vm = new MainViewModel(recorder, new DailyReportService(store), _ => Task.CompletedTask);
        var refresh = vm.RefreshAsync();
        var frame = new DispatcherFrame();
        refresh.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame);
        refresh.GetAwaiter().GetResult();
        var window = new SolidRecords.App.MainWindow(vm);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1188, 720));
        content.Arrange(new Rect(0, 0, 1188, 720));
        content.UpdateLayout();
        var image = new RenderTargetBitmap(1188, 720, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 1188, 720));
        image.Render(background);
        image.Render(content);
        using (var file = File.Create(Path.Combine(directory, "main-window.png")))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(file);
        }
        PresentationTraceSources.DataBindingSource.Flush();
        Require(string.IsNullOrWhiteSpace(bindings.ToString()), "WPF binding errors: " + bindings);
        vm.Dispose();
        Task.Run(async () => await recorder.DisposeAsync()).GetAwaiter().GetResult();
        Console.WriteLine("WPF preview: " + Path.Combine(directory, "main-window.png"));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class EmptySource : IActivitySource
    {
        public string? Warning => null;
        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
