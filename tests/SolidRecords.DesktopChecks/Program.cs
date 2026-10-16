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
            if (args.Contains("--inspect-history", StringComparer.Ordinal)) return HistoryDiagnostics.Run();
            var directory = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? "artifacts/desktop-checks");
            Directory.CreateDirectory(directory);
            CheckWindowFilter();
            if (!args.Contains("--preview-only", StringComparer.Ordinal))
            {
                CheckCaptureAsync(directory).GetAwaiter().GetResult();
                CheckWindowRegistry();
            }
            RenderWindow(directory, args.Contains("--interactive-preview", StringComparer.Ordinal));
            Console.WriteLine("PASS: Requested desktop checks and WPF rendering.");
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

    private static void RenderWindow(string directory, bool interactive)
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
        void Render(string name, double width, double height, double scale)
        {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            var image = new RenderTargetBitmap((int)(width * scale), (int)(height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            image.Render(background);
            image.Render(content);
            using var file = File.Create(Path.Combine(directory, name));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(file);
        }
        Render("main-window.png", 1084, 600, 1);
        Render("main-window-150.png", 1000, 600, 1.5);
        Render("main-window-small-150.png", 744, 380, 1.5);
        var liveCheck = CheckLiveViewAsync(directory);
        var liveFrame = new DispatcherFrame();
        liveCheck.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => liveFrame.Continue = false),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(liveFrame);
        liveCheck.GetAwaiter().GetResult();
        if (interactive)
        {
            window.Title += " · Náhľad";
            window.AllowClose = true;
            window.Loaded += (_, _) => Console.WriteLine($"Interactive preview DPI: {VisualTreeHelper.GetDpi(window).PixelsPerInchX}");
            window.ShowDialog();
        }
        PresentationTraceSources.DataBindingSource.Flush();
        Require(string.IsNullOrWhiteSpace(bindings.ToString()), "WPF binding errors: " + bindings);
        vm.Dispose();
        Task.Run(async () => await recorder.DisposeAsync()).GetAwaiter().GetResult();
        Console.WriteLine("WPF preview: " + Path.Combine(directory, "main-window.png"));
    }

    private static void CheckWindowRegistry()
    {
        // Show ordinary test windows offscreen without activating them. They must have
        // regular document-window styles to pass the production window filter.
        HwndSource CreateWindow(string name)
        {
            var source = new HwndSource(new HwndSourceParameters(name)
            {
                PositionX = -32000, PositionY = -32000, Width = 100, Height = 50,
                WindowStyle = 0x00CF0000
            });
            NativeMethods.ShowWindow(source.Handle, 4); // SW_SHOWNOACTIVATE
            return source;
        }
        using var first = CreateWindow("SolidRecords check first");
        using var second = CreateWindow("SolidRecords check second");
        using var helper = new HwndSource(new HwndSourceParameters("SolidRecords helper menu")
        {
            PositionX = -32000, PositionY = -32000, Width = 100, Height = 50,
            WindowStyle = unchecked((int)0x90000000), ExtendedWindowStyle = 0x08000080
        });
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
        Require(!registry.Contains(helper.Handle), "A tool/menu window entered the process lifetime registry");
        first.Dispose();
        registry.Refresh();
        Require(current.IsOpen, "Process ended while another window remained open");
        second.Dispose();
        registry.Refresh();
        Require(!current.IsOpen, "A remaining helper kept the process open after its last document window closed");
        Console.WriteLine("Native window registry: first closure preserved process; last closure ended it.");
    }

    private static async Task CheckLiveViewAsync(string directory)
    {
        var store = new SqliteActivityStore(Path.Combine(directory, $"live-{Guid.NewGuid():N}.db"));
        var source = new ManualSource();
        var at = new DateTimeOffset(DateTime.Today.AddHours(12));
        var options = new ActivityTrackingOptions(TimeSpan.Zero, TimeSpan.Zero);
        await using var recorder = new Recorder(store, source, new FixedClock(at), options);
        using var viewModel = new MainViewModel(recorder, new DailyReportService(store, options: options), _ => Task.CompletedTask);
        await viewModel.RefreshAsync();
        var resets = 0;
        viewModel.Activities.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };
        async Task Until(Func<bool> condition)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition())
            {
                if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("Live view did not update automatically");
                await Task.Delay(25);
            }
        }
        await recorder.StartAsync();
        var window = new WindowInfo(101, 42, "browser", "Live tab", "https://example.com/one");
        source.Publish(new(SignalKind.SessionActive, at));
        source.Publish(new(SignalKind.ForegroundChanged, at, window with { Url = null }));
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(1)));
        await Until(() => viewModel.Activities.Count == 1);
        var existing = viewModel.Activities[0];
        Require(existing.End == "-" && existing.Duration == "-", "An open activity displays a final end or duration");
        source.Publish(new(SignalKind.ForegroundChanged, at.AddMilliseconds(1500), window));
        await Until(() => existing.Url == window.Url);
        Require(viewModel.Activities.Count == 1 && ReferenceEquals(existing, viewModel.Activities[0]),
            "Late URL resolution created a duplicate row");
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(2)));
        await Until(() => existing.Interval.Duration == TimeSpan.FromSeconds(2));
        Require(ReferenceEquals(existing, viewModel.Activities[0]), "Live duration replaced an existing row");
        source.Publish(new(SignalKind.ForegroundChanged, at.AddSeconds(3), window with { Url = "https://example.com/two" }));
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(4)));
        await Until(() => viewModel.Activities.Count == 2);
        Require(viewModel.Activities[0].Url.EndsWith("/two", StringComparison.Ordinal), "A new URL did not appear live");
        Require(ReferenceEquals(existing, viewModel.Activities[1]), "Adding a URL replaced the previous row");
        Require(existing.End != "-" && existing.Duration != "-", "A closed activity did not display final times");
        Require(viewModel.Activities[0].End == "-" && viewModel.Activities[0].Duration == "-",
            "The new open URL displays a final end or duration");
        await recorder.StopAsync();
        await Until(() => viewModel.Activities.All(row => !row.Interval.IsOpen));
        viewModel.SelectedDate = DateTime.Today.AddDays(-1);
        await Until(() => viewModel.Activities.Count == 0);
        viewModel.SelectedDate = DateTime.Today;
        await Until(() => viewModel.Activities.Count == 2);
        Require(resets == 0, "Live updates reset the collection and lost row selection");
        Console.WriteLine("Live view: automatic insert, duration update, URL change, recording stop and date navigation passed without collection resets.");
        await CheckDefaultActivityRulesAsync(directory);
    }

    private static async Task CheckDefaultActivityRulesAsync(string directory)
    {
        var store = new SqliteActivityStore(Path.Combine(directory, $"thresholds-{Guid.NewGuid():N}.db"));
        var source = new ManualSource();
        var at = new DateTimeOffset(DateTime.Today.AddHours(12));
        await using var recorder = new Recorder(store, source, new FixedClock(at));
        using var vm = new MainViewModel(recorder, new DailyReportService(store), _ => Task.CompletedTask);
        await vm.RefreshAsync();
        async Task Until(Func<Task<bool>> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!await condition())
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("Default activity rules did not reach the expected live state");
                await Task.Delay(25);
            }
        }
        Task<bool> State(Func<bool> predicate) => Task.FromResult(predicate());
        async Task<bool> HeartbeatWritten(int second) => (await store.ReadEventsAsync(at, at.AddDays(1)))
            .Any(e => e.Kind == EventKind.Heartbeat && e.Timestamp == at.AddSeconds(second));
        await recorder.StartAsync();
        var document = new WindowInfo(100, 42, "editor", "Document");
        source.Publish(new(SignalKind.SessionActive, at));
        source.Publish(new(SignalKind.ForegroundChanged, at, document));
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(14)));
        await Until(() => HeartbeatWritten(14));
        Require(vm.Activities.Count == 0 && (await store.ReadIntervalsAsync(at, at.AddDays(1))).Count == 0,
            "A sub-15-second candidate reached the activity log");
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(15)));
        await Until(() => State(() => vm.Activities.Count == 1));
        var row = vm.Activities[0];
        Require(row.Interval.Start == at, "The threshold changed the original start time");
        source.Publish(new(SignalKind.ForegroundChanged, at.AddSeconds(20), document with { Title = "Brief other document" }));
        source.Publish(new(SignalKind.ForegroundChanged, at.AddSeconds(25), document));
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(39)));
        await Until(() => HeartbeatWritten(39));
        await Until(() => State(() => !row.Interval.IsOpen));
        Require(vm.Activities.Count == 1, "A brief intervening document reached the log");
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(40)));
        await Until(() => State(() => row.Interval.IsOpen && row.Interval.Duration == TimeSpan.FromSeconds(35)));
        Require(vm.Activities.Count == 1 && ReferenceEquals(row, vm.Activities[0]), "A merged activity replaced or duplicated its existing row");
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(45)));
        await recorder.StopAsync();
        await Until(() => State(() => !row.Interval.IsOpen && row.Interval.Duration == TimeSpan.FromSeconds(40)));
        Require(row.Interval.Spans.Count == 2, "Merge did not preserve the separate foreground spans");
        Console.WriteLine("Default activity rules: 14 s hidden; 15 s visible; short intervening visit discarded; identical visits merged live with gap excluded.");
    }

    private static void CheckWindowFilter()
    {
        var document = new WindowCandidate("editor", "Document", "EditorWindow", 0, true, true, false);
        var ignored = new[]
        {
            document with { ClassName = "#32768" },
            document with { Application = "SolidRecords.App", ClassName = "WindowsForms10.Window", ExtendedStyle = 0x80 },
            document with { ExtendedStyle = 0x08000000 },
            document with { Application = "explorer", ClassName = "Shell_TrayWnd", Title = "Taskbar" },
            document with { Application = "explorer", ClassName = "MultitaskingViewFrame", Title = "Task switching" },
            document with { Application = "explorer", ClassName = "Windows.UI.Core.CoreWindow" },
            document with { Application = "explorer", ClassName = "WorkerW", Title = "" },
            document with { Application = "LockApp", Title = "Windows lock screen" },
            document with { Application = "LogonUI", Title = "Windows sign-in" },
            document with { Application = "SearchHost", Title = "Search" },
            document with { Title = "" }, document with { IsRoot = false },
            document with { IsVisible = false }, document with { IsCloaked = true }
        };
        foreach (var candidate in ignored)
            Require(!WindowFilter.CanTrackForeground(candidate), "Window filter admitted shell/helper UI: " + candidate);
        var accepted = new[]
        {
            document,
            document with { Application = "explorer", ClassName = "CabinetWClass", Title = "Documents" },
            document with { Application = "explorer", ClassName = "#32770", Title = "Copying files" },
            document with { Application = "explorer", ClassName = "OperationStatusWindow", Title = "Moving files" },
            document with { Application = "ApplicationFrameHost", ClassName = "ApplicationFrameWindow", Title = "Calculator" },
            document with { ClassName = "#32770", Title = "Save as" },
            document with { Application = "SolidRecords.App", ClassName = "HwndWrapper", Title = "SolidRecords · Záznam práce" },
            document with { Application = "chrome", ClassName = "Chrome_WidgetWin_1", Title = "Web page" }
        };
        foreach (var candidate in accepted)
            Require(WindowFilter.CanTrackForeground(candidate), "Window filter rejected a document/dialog: " + candidate);
        Require(WindowFilter.IsUserWindow(document with { IsCloaked = true }), "A window on another desktop lost its lifetime");
        Console.WriteLine($"Window filter: {ignored.Length} shell/helper cases excluded; {accepted.Length} document/dialog cases preserved.");
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at.ToUniversalTime();
    }

    private sealed class ManualSource : IActivitySource
    {
        private Action<CaptureSignal>? _publish;
        public void Publish(CaptureSignal signal) => _publish!(signal);
        public Task StartAsync(Action<CaptureSignal> publish, CancellationToken cancellationToken = default)
        {
            _publish = publish;
            return Task.CompletedTask;
        }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
