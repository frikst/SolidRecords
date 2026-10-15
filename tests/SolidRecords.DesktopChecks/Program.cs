using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
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
            CheckWindowRegistry();
            RenderWindow(directory);
            Console.WriteLine("PASS: Window capture without WMI, composite persistence and WPF rendering.");
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
        // A process without a window must not be enrolled by the foreground-only collector.
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping -n 4 127.0.0.1 >nul")
        { UseShellExecute = false, CreateNoWindow = true })!;
        var pid = process.Id;
        await process.WaitForExitAsync();
        await Task.Delay(2200);
        await recorder.StopAsync();
        var events = await store.ReadEventsAsync(start, DateTimeOffset.UtcNow.AddSeconds(1));
        Require(events.Any(x => x.Kind == EventKind.RecordingStarted), "Missing recording start");
        Require(events.Any(x => x.Kind == EventKind.SessionActive || x.Kind == EventKind.SessionInactive), "Missing session baseline");
        var composites = await store.ReadProcessesAsync(start, DateTimeOffset.UtcNow.AddSeconds(1));
        Require(composites.All(x => x.ProcessId != pid), "A process without a foreground window was enrolled");
        Require(composites.All(x => !x.IsOpen), "Recording stop did not close observed process envelopes");
        Require(composites.SelectMany(x => x.Windows).All(x => !x.IsOpen), "Recording stop did not close window envelopes");
        Require(events.Last().Kind == EventKind.RecordingStopped, "Recording did not drain on stop");
        Console.WriteLine($"Capture: {composites.Count} observed processes; {events.Count} internal events; no WMI.");
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
        // Load only the view resources. Never run the real application's startup,
        // single-instance registration or user database initialization from a preview.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SolidRecords.App;component/Resources/Styles.xaml")
        });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var store = new SqliteActivityStore(Path.Combine(directory, $"preview-{Guid.NewGuid():N}.db"));
        var day = new DateTimeOffset(DateTime.Today.AddHours(8));
        var samples = new[]
        {
            ("msedge", "Projekt SolidRecords · dokumentácia", "https://learn.microsoft.com/dotnet/", 0, 38),
            ("devenv", "SolidRecords – Microsoft Visual Studio", (string?)null, 38, 74),
            ("WindowsTerminal", "PowerShell · dotnet test", (string?)null, 112, 12),
            ("msedge", "WPF · UI Automation", "https://learn.microsoft.com/dotnet/desktop/wpf/", 124, 26),
            ("devenv", "MainWindow.xaml – SolidRecords", (string?)null, 165, 55),
            ("notepad", "Poznámky k projektu.txt", (string?)null, 220, 8)
        };
        var engine = new RecordingEngine();
        void Save(CaptureSignal signal) => store.SaveAsync(engine.Apply(signal)).GetAwaiter().GetResult();
        Save(new(SignalKind.RecordingStarted, day));
        Save(new(SignalKind.SessionActive, day));
        var apps = samples.Select(x => x.Item1).Distinct().Select((name, i) => (name, id: i + 20)).ToDictionary(x => x.name, x => x.id);
        foreach (var sample in samples)
        {
            var pid = apps[sample.Item1];
            Save(new(SignalKind.ForegroundChanged, day.AddMinutes(sample.Item4),
                new(pid + 100, pid, sample.Item1, sample.Item2, sample.Item3)));
            Save(new(SignalKind.ForegroundChanged, day.AddMinutes(sample.Item4 + sample.Item5)));
        }
        Save(new(SignalKind.RecordingStopped, day.AddMinutes(228)));
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

    private static void CheckWindowRegistry()
    {
        // Offscreen, nonactivating native windows exercise actual EnumWindows behavior
        // without taking focus or putting test windows on the user's taskbar.
        HwndSource CreateWindow(string name) => new(new HwndSourceParameters(name)
        {
            PositionX = -32000, PositionY = -32000, Width = 100, Height = 50,
            WindowStyle = unchecked((int)0x90000000), ExtendedWindowStyle = 0x08000080
        });
        using var first = CreateWindow("SolidRecords check first");
        using var second = CreateWindow("SolidRecords check second");
        using var self = Process.GetCurrentProcess();
        var info = new WindowInfo(first.Handle, self.Id, self.ProcessName, "SolidRecords check first",
            ProcessStartTicks: self.StartTime.ToUniversalTime().Ticks);
        var engine = new RecordingEngine();
        var at = DateTimeOffset.UtcNow;
        engine.Apply(new(SignalKind.RecordingStarted, at));
        engine.Apply(new(SignalKind.SessionActive, at));
        var current = engine.Apply(new(SignalKind.ForegroundChanged, at, info)).Processes.Single();
        var registry = new ObservedWindowRegistry(signal =>
        {
            var batch = engine.Apply(signal);
            foreach (var process in batch.Processes) current = process;
        });
        registry.ObserveForeground(info);
        Require(registry.Contains(first.Handle) && registry.Contains(second.Handle), "Native window discovery missed a window");
        first.Dispose();
        registry.Refresh();
        Require(current.IsOpen, "Process ended while another window remained open");
        second.Dispose();
        registry.Refresh();
        Require(!current.IsOpen, "Process remained open after its last native window closed");
        Console.WriteLine("Native window registry: first closure preserved process; last closure ended it.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class EmptySource : IActivitySource
    {
        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
