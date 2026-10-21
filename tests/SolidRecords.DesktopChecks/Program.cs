using SolidRecords.Core.Localization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Core.Recording;
using SolidRecords.Core.Reporting;
using SolidRecords.Storage;
using SolidRecords.Windows.Capture;
using SolidRecords.Windows.Interop;

namespace SolidRecords.DesktopChecks;

/// <summary>Opt-in Windows integration checks. Uses isolated databases, never the user's history.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Strings.Apply(args.Contains("--english", StringComparer.Ordinal) ? ApplicationLanguage.English : ApplicationLanguage.Slovak);
            if (args.Contains("--updates-only", StringComparer.Ordinal))
            {
                UpdateChecks.RunAsync().GetAwaiter().GetResult();
                return 0;
            }
            if (args.Contains("--startup-only", StringComparer.Ordinal))
                return StartupChecks.Run(args.Contains("--paused", StringComparer.Ordinal));
            if (args.Contains("--inspect-history", StringComparer.Ordinal)) return HistoryDiagnostics.Run();
            var directory = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? "artifacts/desktop-checks");
            Directory.CreateDirectory(directory);
            SessionCaptureChecks.Run();
            if (args.Contains("--session-only", StringComparer.Ordinal)) return 0;
            CheckWindowFilter();
            if (!args.Contains("--preview-only", StringComparer.Ordinal))
            {
                CheckCaptureAsync(directory).GetAwaiter().GetResult();
                CheckWindowRegistry();
            }
            RenderWindow(directory, args.Contains("--interactive-preview", StringComparer.Ordinal),
                args.Contains("--search-preview", StringComparer.Ordinal));
            Console.WriteLine("PASS: Requested desktop checks and WPF rendering.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void CheckSorting(System.Windows.Controls.DataGrid grid)
    {
        var source = grid.ItemsSource;
        var start = DateTimeOffset.Now;
        ActivityRow Row(int seconds, bool open = false) => new(new(
            Guid.NewGuid(), Guid.NewGuid(), new(100, seconds, $"App {seconds}", "Sorting sample"),
            start, start.AddSeconds(seconds), open));
        var rows = new System.Collections.ObjectModel.ObservableCollection<ActivityRow>(
            new[] { Row(600), Row(90), Row(3600), Row(20), Row(120, true), Row(9) });
        grid.ItemsSource = rows;
        var sorting = typeof(System.Windows.Controls.DataGrid).GetMethod("OnSorting",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Click(System.Windows.Controls.DataGridColumn column) =>
            sorting.Invoke(grid, new object[] { new System.Windows.Controls.DataGridSortingEventArgs(column) });
        ActivityRow[] Visible() => grid.Items.Cast<ActivityRow>().ToArray();
        void Drain() => grid.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var duration = grid.Columns.Single(column => Equals(column.Header, Strings.Get("Duration")));
        Click(duration);
        Require(Visible().SequenceEqual(rows.OrderBy(row => row.Interval.Duration)), "Duration ASC used formatted text");
        Click(duration);
        Require(Visible().SequenceEqual(rows.OrderByDescending(row => row.Interval.Duration)), "Duration DESC used formatted text");
        Click(duration);
        Require(Visible().SequenceEqual(rows) && grid.Columns.All(column => column.SortDirection is null),
            "Third click did not restore source order and clear arrows");

        foreach (var column in grid.Columns)
        {
            Click(column);
            Require(column.SortDirection == System.ComponentModel.ListSortDirection.Ascending, "First click is not ASC");
            Click(column);
            Require(column.SortDirection == System.ComponentModel.ListSortDirection.Descending, "Second click is not DESC");
            Click(column);
            Require(grid.Items.SortDescriptions.Count == 0 && Visible().SequenceEqual(rows), "Column sort did not clear");
        }

        Click(duration);
        var growing = rows[1];
        growing.Update(growing.Interval with { End = start.AddHours(2), IsOpen = true });
        rows.Insert(0, Row(45));
        Drain();
        Require(Visible().SequenceEqual(rows.OrderBy(row => row.Interval.Duration)), "Live duration update or insert lost numeric ordering");
        var application = grid.Columns[0];
        Click(application);
        Require(duration.SortDirection is null && grid.Items.SortDescriptions.Count == 1,
            "Switching columns retained the previous sort");
        Click(application);
        Click(application);
        rows.Move(0, rows.Count - 1);
        Require(Visible().SequenceEqual(rows), "Cleared sorting did not follow source updates");
        grid.ItemsSource = source;
        Console.WriteLine("Sorting: numeric durations, all column cycles, column switch, live updates and original order passed.");
    }
    private static void CheckSessionTimeAlignment(System.Windows.Controls.DataGrid grid)
    {
        var rows = Descendants<System.Windows.Controls.DataGridRow>(grid).ToArray();
        var activity = rows.FirstOrDefault(row => row.Item is ActivityRow);
        if (activity is null) return;
        var start = grid.Columns[4].GetCellContent(activity.Item);
        if (start is null) return;
        var expected = start.TransformToAncestor(grid).Transform(new Point()).X;
        foreach (var row in rows.Where(row => row.Item is SessionEventRow))
        {
            var time = (FrameworkElement)row.Template.FindName("SessionTime", row);
            var actual = time.TransformToAncestor(grid).Transform(new Point()).X;
            Require(Math.Abs(actual - expected) <= 1, $"Separator time is not aligned: {actual} vs {expected}");
        }
    }

    private static void CheckActivityRowColors(System.Windows.Controls.DataGrid grid)
    {
        foreach (var activity in grid.Items.OfType<ActivityRow>().ToArray())
        {
            grid.ScrollIntoView(activity);
            grid.UpdateLayout();
            var row = (System.Windows.Controls.DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(activity);
            var expectedBackground = row.IsSelected ? (Brush)grid.FindResource("AccentFillColorDefaultBrush")
                : System.Windows.Controls.ItemsControl.GetAlternationIndex(row) == 1 ? grid.AlternatingRowBackground : grid.RowBackground;
            var expectedForeground = (Brush)grid.FindResource(row.IsSelected
                ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
            Require(((SolidColorBrush)row.Background).Color == ((SolidColorBrush)expectedBackground).Color,
                "An activity row retained a default background instead of the current theme");
            Require(((SolidColorBrush)row.Foreground).Color == ((SolidColorBrush)expectedForeground).Color,
                "An activity row retained a default foreground instead of the current theme");
            foreach (var text in Descendants<System.Windows.Controls.TextBlock>(row))
                Require(((SolidColorBrush)text.Foreground).Color == ((SolidColorBrush)expectedForeground).Color,
                    "Activity cell text does not use the row's current theme foreground");
        }
        grid.ScrollIntoView(grid.SelectedItem ?? grid.Items[0]);
        grid.UpdateLayout();
    }

    private static void CheckSessionNavigation(SolidRecords.App.MainWindow window, System.Windows.Controls.DataGrid grid)
    {
        var source = grid.ItemsSource;
        var selected = grid.SelectedItem;
        var column = grid.Columns[2];
        var at = DateTimeOffset.Now;
        var rows = new System.Collections.ObjectModel.ObservableCollection<IActivityListRow>();
        SessionEventRow Separator() => new(new(Guid.NewGuid(), Guid.NewGuid(), EventKind.SessionInactive, at, Reason: "WTS:7"));
        rows.Add(Separator());
        for (var index = 0; index < 40; index++)
        {
            rows.Add(new ActivityRow(new(Guid.NewGuid(), Guid.NewGuid(), new(100, index, $"App {index:00}", "Navigation"),
                at, at.AddSeconds(20), false)));
            rows.Add(Separator());
            rows.Add(Separator());
        }
        window.Show();
        grid.ItemsSource = rows;
        grid.CurrentColumn = column;
        grid.SelectedItem = null;
        grid.UpdateLayout();
        void Press(System.Windows.Input.Key key)
        {
            var args = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(grid), 0, key) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            var target = grid.SelectedItem is ActivityRow activity ? column.GetCellContent(activity) : null;
            ((UIElement?)target ?? grid).RaiseEvent(args);
            Require(args.Handled, "The activity navigation key was not handled");
        }
        void CheckOrder()
        {
            var activities = grid.Items.OfType<ActivityRow>().ToArray();
            grid.SelectedItem = null;
            foreach (var activity in activities)
            {
                Press(System.Windows.Input.Key.Down);
                Require(ReferenceEquals(grid.SelectedItem, activity) && ReferenceEquals(grid.CurrentCell.Item, activity)
                    && grid.CurrentColumn == column, "Down did not skip separators or preserve the current column");
            }
            Press(System.Windows.Input.Key.Down);
            Require(ReferenceEquals(grid.SelectedItem, activities[^1]), "Down crossed the last activity");
            foreach (var activity in activities.Reverse().Skip(1))
            {
                Press(System.Windows.Input.Key.Up);
                Require(ReferenceEquals(grid.SelectedItem, activity), "Up did not skip separators");
            }
            Press(System.Windows.Input.Key.Up);
            Require(ReferenceEquals(grid.SelectedItem, activities[0]), "Up crossed the first activity");
            grid.SelectedItem = null;
            Press(System.Windows.Input.Key.Up);
            Require(ReferenceEquals(grid.SelectedItem, activities[^1]), "Up without selection did not choose the last activity");
        }
        CheckOrder();
        grid.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(ActivityRow.Application),
            System.ComponentModel.ListSortDirection.Descending));
        CheckOrder();
        grid.Items.SortDescriptions.Clear();
        grid.Items.Filter = item => item is SessionEventRow || ((ActivityRow)item).ProcessId % 3 == 0;
        CheckOrder();
        grid.Items.Filter = item => item is SessionEventRow;
        grid.SelectedItem = null;
        Press(System.Windows.Input.Key.Down);
        Press(System.Windows.Input.Key.Up);
        Require(grid.SelectedItem is null, "Separators were selected in a list without activities");
        grid.Items.Filter = null;
        grid.ItemsSource = source;
        grid.SelectedItem = selected;
        window.Hide();
        Console.WriteLine("Activity navigation: Up/Down skip consecutive separators, preserve the column and cross virtualized rows in original, sorted and filtered order.");
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

    private static void RenderWindow(string directory, bool interactive, bool searchPreview)
    {
        var bindings = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindings));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        // Load only the view resources. Never run the real application's startup,
        // single-instance registration or user database initialization from a preview.
#pragma warning disable WPF0001 // Exercise the same built-in Fluent theme mode as App.xaml.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Light };
        app.Resources.MergedDictionaries.Add(new LocalizationDictionary());
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
        var previewIcon = new ApplicationIconReader().Read(new(0, Environment.ProcessId, "preview", "Preview"));
        Require(previewIcon is not null, "The executable icon could not be collected");
        store.SaveAsync(new([], [])
        {
            ApplicationIcons = samples.Select(x => x.Item1).Distinct().Select(name => previewIcon! with { Application = name }).ToArray()
        }).GetAwaiter().GetResult();
        var engine = new RecordingEngine();
        void Save(CaptureSignal signal) => store.SaveAsync(engine.Apply(signal)).GetAwaiter().GetResult();
        Save(new(SignalKind.RecordingStarted, day));
        Save(new(SignalKind.SessionActive, day, Reason: "ApplicationStartup"));
        var apps = samples.Select(x => x.Item1).Distinct().Select((name, i) => (name, id: i + 20)).ToDictionary(x => x.name, x => x.id);
        foreach (var sample in samples)
        {
            var pid = apps[sample.Item1] + (sample.Item1 == "msedge" && sample.Item4 > 0 ? 1000 : 0);
            Save(new(SignalKind.ForegroundChanged, day.AddMinutes(sample.Item4),
                new(pid + 100, pid, sample.Item1, sample.Item2, sample.Item3)));
            Save(new(SignalKind.ForegroundChanged, day.AddMinutes(sample.Item4 + sample.Item5)));
        }
        Save(new(SignalKind.SessionInactive, day.AddMinutes(228), Reason: "WTS:7"));
        Save(new(SignalKind.SessionActive, day.AddMinutes(240), Reason: "WTS:8"));
        Save(new(SignalKind.SessionInactive, day.AddMinutes(250), Reason: "Shutdown"));
        Save(new(SignalKind.RecordingStopped, day.AddMinutes(250)));
        store.SaveAsync(new(Enumerable.Range(0, 4).Select(index => new SolidRecords.Core.ActivityEvent(Guid.NewGuid(), Guid.NewGuid(),
            index == 0 ? EventKind.SessionInactive : EventKind.SessionActive, day.AddMinutes(index * 20 + 1),
            Reason: "RecordingBaseline")).ToArray(), [])).GetAwaiter().GetResult();
        var recorder = new Recorder(store, new EmptySource());
        var vm = new MainViewModel(recorder, new DailyReportService(store));
        var refresh = vm.RefreshAsync();
        var frame = new DispatcherFrame();
        refresh.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame);
        refresh.GetAwaiter().GetResult();
        Require(vm.Activities.All(row => row.Icon is { IsFrozen: true }), "Stored icons were not decoded and frozen");
        var separators = vm.EventRows.OfType<SessionEventRow>().ToArray();
        Require(separators.Length == 4 && separators[0].Description == Strings.Get("Shutdown")
            && separators[1].Description == Strings.Get("Unlock")
            && separators[2].Description == Strings.Get("Lock")
            && separators[3].Summary == Strings.Get("ActiveSession") + " — " + Strings.Get("ApplicationStartup"), "Session separators lost their order or type");
        var separator = separators[0];
        var refreshSeparators = vm.RefreshAsync();
        var separatorFrame = new DispatcherFrame();
        refreshSeparators.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => separatorFrame.Continue = false),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(separatorFrame);
        refreshSeparators.GetAwaiter().GetResult();
        Require(ReferenceEquals(separator, vm.EventRows[0]), "Refresh replaced a session separator");
        vm.Search = "no-matching-activity";
        Require(vm.Activities.Count == 0 && vm.EventRows.Count == 4 && !vm.IsEmpty,
            "Search removed session context or counted separators as activities");
        vm.Search = "";
        var window = new SolidRecords.App.MainWindow(vm);
        var content = (FrameworkElement)window.Content;
        void Render(string name, double width, double height, double scale)
        {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            CheckSessionTimeAlignment(Descendants<System.Windows.Controls.DataGrid>(content).Single());
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
        void RenderFocusedSearch(string name)
        {
            if (!searchPreview) return;
            vm.Search = "SolidRecords";
            window.Show();
            window.Activate();
            var search = Descendants<System.Windows.Controls.TextBox>(content)
                .Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == Strings.Get("SearchActivities"));
            search.Focus();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Require(search.IsKeyboardFocusWithin, "Search preview did not receive keyboard focus");
            var bounds = new Rect(0, 0, search.ActualWidth, search.ActualHeight);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle((Brush)app.FindResource("ControlSolidFillColorDefaultBrush"), null, bounds);
                drawing.DrawRectangle(new VisualBrush(search) { Stretch = Stretch.Fill }, null, bounds);
            }
            var image = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * 1.5),
                (int)Math.Ceiling(bounds.Height * 1.5), 144, 144, PixelFormats.Pbgra32);
            image.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(directory, name));
            encoder.Save(file);
            Console.WriteLine($"Focused search preview DPI: {VisualTreeHelper.GetDpi(window).PixelsPerInchX}");
            vm.ClearSearchCommand.Execute(null);
        }
        void RenderCalendar(string theme)
        {
            var mainPicker = Descendants<System.Windows.Controls.DatePicker>(content).Single();
            Require(ReferenceEquals(mainPicker.CalendarStyle, app.FindResource("ActivityCalendar")),
                "The activity day style is not applied to the main date picker");
            var picker = new System.Windows.Controls.DatePicker
            {
                Style = mainPicker.Style,
                CalendarStyle = mainPicker.CalendarStyle,
                SelectedDate = DateTime.Today,
                DisplayDate = DateTime.Today
            };
            ActivityDatePicker.SetAvailableDates(picker, [DateTime.Today, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(-5)]);
            var host = new Window { Content = picker, Width = 240, Height = 80, ShowInTaskbar = false, ShowActivated = false };
            host.Show();
            picker.IsDropDownOpen = true;
            picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var popup = (System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup", picker);
            var popupContent = (FrameworkElement)popup.Child;
            popupContent.UpdateLayout();
            var calendar = popupContent as System.Windows.Controls.Calendar
                ?? Descendants<System.Windows.Controls.Calendar>(popupContent).Single();
            void CheckCalendarLanguage()
            {
                calendar.UpdateLayout();
                var culture = Strings.Culture;
                Require(string.Equals(calendar.Language.IetfLanguageTag, culture.IetfLanguageTag, StringComparison.OrdinalIgnoreCase),
                    "Popup calendar did not receive the application language.");
                Require(calendar.FirstDayOfWeek == culture.DateTimeFormat.FirstDayOfWeek,
                    "Popup calendar uses the wrong first day of week.");
                var item = Descendants<System.Windows.Controls.Primitives.CalendarItem>(calendar).Single();
                var header = (System.Windows.Controls.Button)item.Template.FindName("PART_HeaderButton", item);
                Require(Equals(header.Content, calendar.DisplayDate.ToString("Y", culture)),
                    $"Calendar month heading did not change language: {header.Content}.");
                var month = (System.Windows.Controls.Grid)item.Template.FindName("PART_MonthView", item);
                var dayTitles = month.Children.OfType<System.Windows.Controls.TextBlock>()
                    .Where(text => System.Windows.Controls.Grid.GetRow(text) == 0)
                    .OrderBy(System.Windows.Controls.Grid.GetColumn).Select(text => text.Text).ToArray();
                var expected = Enumerable.Range(0, 7).Select(index => culture.DateTimeFormat.ShortestDayNames[
                    ((int)calendar.FirstDayOfWeek + index) % 7]);
                Require(dayTitles.SequenceEqual(expected), "Calendar weekday names did not change language.");
            }
            var originalLanguage = Strings.Culture.TwoLetterISOLanguageName == "sk" ? ApplicationLanguage.Slovak : ApplicationLanguage.English;
            foreach (var language in new[] { originalLanguage, ApplicationLanguage.English, ApplicationLanguage.Slovak, originalLanguage })
            {
                var selectedDate = picker.SelectedDate;
                var displayDate = calendar.DisplayDate;
                var blackoutCount = calendar.BlackoutDates.Count;
                LocalizationDictionary.Apply(language);
                picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                CheckCalendarLanguage();
                Require(picker.IsDropDownOpen && picker.SelectedDate == selectedDate && calendar.DisplayDate == displayDate
                    && calendar.BlackoutDates.Count == blackoutCount, "Changing language reset the open calendar or date restrictions.");
            }
            picker.IsDropDownOpen = false;
            picker.IsDropDownOpen = true;
            picker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            CheckCalendarLanguage();
            var days = Descendants<System.Windows.Controls.Primitives.CalendarDayButton>(popupContent)
                .Where(button => button.Visibility == Visibility.Visible).ToArray();
            var blocked = days.First(button => button.IsBlackedOut);
            var available = days.First(button => !button.IsBlackedOut && !button.IsSelected);
            var selectedDay = days.Single(button => button.IsSelected);
            Require(!blocked.IsHitTestVisible && !blocked.Focusable && available.IsHitTestVisible,
                "Unavailable calendar days still react to pointer input or keyboard focus");
            Require(available.FontWeight == FontWeights.SemiBold && blocked.FontWeight == FontWeights.Normal
                && !Equals(available.Foreground, blocked.Foreground),
                "Available calendar days are not visually distinct");
            Require(!Equals(selectedDay.Background, available.Background), "The selected day has no distinct fill");
            foreach (var scale in new[] { 1.0, 1.5 })
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(popupContent.ActualWidth * scale),
                    (int)Math.Ceiling(popupContent.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(popupContent);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"calendar-{theme}-{scale * 100:0}.png"));
                encoder.Save(file);
            }
            picker.IsDropDownOpen = false;
            host.Close();
        }
        Render("main-window.png", 1084, 600, 1);
        RenderCalendar("light");
        var originalLanguage = Strings.Culture.TwoLetterISOLanguageName == "sk" ? ApplicationLanguage.Slovak : ApplicationLanguage.English;
        var datePicker = Descendants<System.Windows.Controls.DatePicker>(content).Single();
        var dateTextBox = (System.Windows.Controls.Primitives.DatePickerTextBox)datePicker.Template.FindName("PART_TextBox", datePicker);
        var originalDate = datePicker.SelectedDate;
        var dateChanges = 0;
        datePicker.SelectedDateChanged += (_, _) => dateChanges++;
        var originalRows = vm.Activities.ToArray();
        var originalSelection = vm.SelectedActivity;
        foreach (var language in new[] { ApplicationLanguage.English, ApplicationLanguage.Slovak, originalLanguage })
        {
            LocalizationDictionary.Apply(language);
            vm.ApplySettings(new ApplicationSettings(Language: language));
            window.UpdateLayout();
            datePicker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var expectedDateText = originalDate!.Value.ToString(datePicker.SelectedDateFormat == System.Windows.Controls.DatePickerFormat.Short ? "d" : "D", Strings.Culture);
            Require(!datePicker.IsDropDownOpen && datePicker.Text == expectedDateText && dateTextBox.Text == expectedDateText,
                $"Closed date picker did not reformat immediately: {dateTextBox.Text}; expected {expectedDateText}.");
            Require(datePicker.SelectedDate == originalDate && vm.SelectedDate == originalDate && dateChanges == 0,
                "Reformatting the date changed the selected day or raised a selection event.");
            Require(window.Title == Strings.Get("MainTitle"), "Existing window title did not change language.");
            Require(Descendants<System.Windows.Controls.DataGrid>(content).Single().Columns.Any(column => Equals(column.Header, Strings.Get("Duration"))), "Column header did not change language.");
            Require(vm.Activities.SequenceEqual(originalRows) && ReferenceEquals(vm.SelectedActivity, originalSelection),
                "Language change replaced activity rows or selection.");
            Require(string.Equals(window.Language.IetfLanguageTag, Strings.Culture.IetfLanguageTag, StringComparison.OrdinalIgnoreCase), "Window formatting language did not change.");
        }
        Render("main-window-150.png", 1000, 600, 1.5);
        Render("main-window-small-150.png", 744, 380, 1.5);
        var timeline = Descendants<TimelineControl>(content).Single();
        var grid = Descendants<System.Windows.Controls.DataGrid>(content).Single();
        CheckSessionTimeAlignment(grid);
        var activityScroll = Descendants<System.Windows.Controls.ScrollViewer>(grid).First(viewer => viewer.TemplatedParent == grid);
        activityScroll.ScrollToHorizontalOffset(120);
        grid.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        grid.UpdateLayout();
        CheckSessionTimeAlignment(grid);
        activityScroll.ScrollToHorizontalOffset(0);
        grid.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        CheckSorting(grid);
        var sessionSorting = typeof(System.Windows.Controls.DataGrid).GetMethod("OnSorting",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var column in grid.Columns)
        {
            for (var click = 0; click < 3; click++)
                sessionSorting.Invoke(grid, new object[] { new System.Windows.Controls.DataGridSortingEventArgs(column) });
            Require(grid.Items.Cast<IActivityListRow>().SequenceEqual(vm.EventRows),
                "Mixed session/activity rows did not restore their chronological order");
        }
        var detailToggle = Descendants<System.Windows.Controls.Primitives.ToggleButton>(content)
            .Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == Strings.Get("ApplicationDetails"));
        detailToggle.IsChecked = true;
        Require(vm.DetailsVisible, "Details toggle did not update the view model");
        grid.SelectedItem = vm.Activities.First(row => row.Application == "msedge");
        var detailApp = vm.DetailApplications.Single();
        Require(detailApp.Label == "msedge" && detailApp.Children.Count == 2,
            "Details did not group all application processes");
        Require(detailApp.Children.Sum(process => process.Children.Count) == 2,
            "Details did not include all process activities");
        vm.Search = "Projekt SolidRecords";
        Require(vm.Activities.Count == 1 && ReferenceEquals(detailApp, vm.DetailApplications.Single())
            && detailApp.Children.Count == 2, "Search removed sibling processes from details or replaced the application node");
        Render("main-window-details.png", 1084, 600, 1);
        Render("main-window-details-150.png", 1000, 600, 1.5);
        Render("main-window-details-small-150.png", 744, 380, 1.5);
        vm.Search = "no-matching-activity";
        Require(vm.SelectedActivity is null && vm.DetailApplications.Count == 0, "Empty search left stale application details");
        vm.Search = "";
        grid.SelectedItem = vm.Activities.First(row => row.Application == "devenv");
        Require(vm.DetailApplications.Single().Label == "devenv"
            && vm.DetailApplications.Single().Children.Single().Children.Count == 2,
            "Selection did not switch application details");
        detailToggle.IsChecked = false;
        Require(!vm.DetailsVisible, "Details toggle did not close the panel");
        grid.SelectedItem = vm.Activities[0];
        CheckActivityRowColors(grid);
        var selected = grid.SelectedItem;
        var lightBackground = ((SolidColorBrush)window.Background).Color;
        var lightLabel = ((SolidColorBrush)timeline.LabelBrush).Color;
        app.ThemeMode = ThemeMode.Dark;
        vm.DetailsVisible = true;
        Render("main-window-details-dark-150.png", 1000, 600, 1.5);
        vm.DetailsVisible = false;
        Render("main-window-dark.png", 1084, 600, 1);
        CheckActivityRowColors(grid);
        RenderCalendar("dark");
        Render("main-window-dark-150.png", 1000, 600, 1.5);
        Render("main-window-dark-small-150.png", 744, 380, 1.5);
        Require(((SolidColorBrush)window.Background).Color != lightBackground, "Theme switch did not update the window background");
        Require(((SolidColorBrush)timeline.LabelBrush).Color != lightLabel, "Theme switch did not update timeline labels");
        Require(ReferenceEquals(selected, grid.SelectedItem), "Theme switch lost row selection");
        app.ThemeMode = ThemeMode.Light;
        Render("main-window-light-restored.png", 1084, 600, 1);
        Require(((SolidColorBrush)window.Background).Color == lightBackground, "Switching back did not restore the light theme");
        Require(((SolidColorBrush)timeline.LabelBrush).Color == lightLabel, "Switching back did not restore timeline labels");
        CheckActivityRowColors(grid);
        CheckWindowCaption(window);
        CheckSessionNavigation(window, grid);
        if (searchPreview)
        {
            RenderFocusedSearch("main-window-search-light-150.png");
            app.ThemeMode = ThemeMode.Dark;
            RenderFocusedSearch("main-window-search-dark-150.png");
            window.Hide();
        }
        var about = new SolidRecords.App.AboutWindow();
        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            app.ThemeMode = theme;
            var settingsDialog = new SolidRecords.App.SettingsWindow(new SettingsViewModel(new ApplicationSettings(), _ => Task.CompletedTask,
                () => Task.FromResult("Nie je dostupná novšia verzia na inštaláciu.")));
            settingsDialog.Show();
            settingsDialog.UpdateLayout();
            Console.WriteLine($"Settings dialog actual DPI: {VisualTreeHelper.GetDpi(settingsDialog).PixelsPerInchX}");
            var settingsTabs = (System.Windows.Controls.TabControl)settingsDialog.FindName("SettingsTabs");
            foreach (var (tabIndex, tabName) in new[] { (0, "general"), (1, "recording"), (2, "work-hours"), (3, "updates"), (4, "shortcuts") })
            {
                settingsTabs.SelectedIndex = tabIndex;
                settingsDialog.UpdateLayout();
                if (tabIndex == 1)
                {
                    var retentionMode = (System.Windows.Controls.ComboBox)settingsDialog.FindName("RetentionMode");
                    var retentionEditor = (SettingsViewModel)settingsDialog.DataContext;
                    Require(retentionMode.SelectedIndex == 0, "Retention must display Never by default.");
                    retentionMode.SelectedIndex = 1;
                    Require(retentionEditor.DeleteOldActivities, "Retention selector did not update the editor.");
                    var retentionDays = (System.Windows.Controls.TextBox)settingsDialog.FindName("RetentionDays");
                    Require(retentionDays.IsVisible, "Retention days must be visible for automatic deletion.");
                    retentionMode.SelectedIndex = 0;
                    Require(!retentionEditor.DeleteOldActivities, "Never must disable retention.");
                    Require(!retentionDays.IsVisible, "Never must hide retention days.");
                }
                if (tabIndex == 3)
                {
                    var updateNow = (System.Windows.Controls.Button)settingsDialog.FindName("UpdateNow");
                    Require(updateNow.IsVisible && updateNow.Command?.CanExecute(null) == true, "Manual update button is not available.");
                    updateNow.Command!.Execute(null);
                    Require(((SettingsViewModel)settingsDialog.DataContext).UpdateStatus == "Nie je dostupná novšia verzia na inštaláciu.",
                        "Manual update button did not invoke its command.");
                    settingsDialog.UpdateLayout();
                }
                var settingsContent = (FrameworkElement)settingsDialog.Content;
                var settingsSize = new Size(settingsContent.ActualWidth, settingsContent.ActualHeight);
                foreach (var settingsScale in new[] { 1.0, 1.5 })
                {
                    var settingsVisual = new DrawingVisual();
                    using (var drawing = settingsVisual.RenderOpen()) drawing.DrawRectangle(settingsDialog.Background, null, new Rect(settingsSize));
                    var settingsBitmap = new RenderTargetBitmap((int)Math.Ceiling(settingsSize.Width * settingsScale),
                        (int)Math.Ceiling(settingsSize.Height * settingsScale), 96 * settingsScale, 96 * settingsScale, PixelFormats.Pbgra32);
                    settingsBitmap.Render(settingsVisual);
                    settingsBitmap.Render(settingsContent);
                    var settingsEncoder = new PngBitmapEncoder();
                    settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                    using var settingsFile = File.Create(Path.Combine(directory, $"settings-{tabName}-{theme.ToString().ToLowerInvariant()}-{settingsScale * 100:0}.png"));
                    settingsEncoder.Save(settingsFile);
                }
            }
            settingsDialog.Close();
            var aboutContent = (FrameworkElement)about.Content;
            aboutContent.Measure(new Size(480, double.PositiveInfinity));
            var size = new Size(480, aboutContent.DesiredSize.Height);
            aboutContent.Arrange(new Rect(size));
            aboutContent.UpdateLayout();
            foreach (var scale in new[] { 1.0, 1.5 })
            {
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(about.Background, null, new Rect(size));
                }
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale),
                    (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Render(aboutContent);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"about-{theme.ToString().ToLowerInvariant()}-{scale * 100:0}.png"));
                encoder.Save(file);
            }
        }
        about.Show();
        about.UpdateLayout();
        Console.WriteLine($"About dialog actual DPI: {VisualTreeHelper.GetDpi(about).PixelsPerInchX}");
        about.Close();
        app.ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        Console.WriteLine("Themes: light/dark/light switched on the existing window; timeline colors and selection verified; 100% and 150% previews rendered.");
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
#pragma warning disable WPF0001
        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            app.ThemeMode = theme;
            TrayMenuPreview.Render(directory, theme.ToString().ToLowerInvariant());
        }
        app.ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        PresentationTraceSources.DataBindingSource.Flush();
        Require(string.IsNullOrWhiteSpace(bindings.ToString()), "WPF binding errors: " + bindings);
        vm.Dispose();
        Task.Run(async () => await recorder.DisposeAsync()).GetAwaiter().GetResult();
        Console.WriteLine("WPF preview: " + Path.Combine(directory, "main-window.png"));
    }

    private static void CheckWindowCaption(SolidRecords.App.MainWindow window)
    {
        var originalWidth = window.Width;
        window.ShowActivated = false;
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        void Check(FrameworkElement element, Point point, nint expected, string description)
        {
            var screen = element.PointToScreen(point);
            var coordinates = unchecked((nint)(((int)screen.X & 0xffff) | ((int)screen.Y << 16)));
            var actual = SendMessage(handle, 0x0084, 0, coordinates); // WM_NCHITTEST
            Require(actual == expected, $"Caption hit test ({description}): expected {expected}, got {actual}");
        }
        foreach (var width in new[] { 1100.0, 760.0 })
        {
            window.Width = width;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var header = (FrameworkElement)window.FindName("WindowHeader");
            Check(header, new Point(100, 24), 2, "first row"); // HTCAPTION
            var stats = Descendants<System.Windows.Controls.Primitives.UniformGrid>(header).Single();
            foreach (var statistic in stats.Children.Cast<FrameworkElement>())
                Check(statistic, new Point(statistic.ActualWidth / 2, statistic.ActualHeight / 2), 2, "statistic");
            Check(header, new Point(20, header.ActualHeight - 4), 2, "below logo");
            var picker = Descendants<System.Windows.Controls.DatePicker>(header).Single();
            Check(picker, new Point(picker.ActualWidth / 2, picker.ActualHeight / 2), 1, "date picker"); // HTCLIENT
            foreach (var button in Descendants<System.Windows.Controls.Button>(header).Where(button => button.IsEnabled))
                Check(button, new Point(button.ActualWidth / 2, button.ActualHeight / 2), 1, "header button");
            var timeline = Descendants<TimelineControl>(window).Single();
            Check(timeline, new Point(timeline.ActualWidth / 2, timeline.ActualHeight / 2), 1, "timeline");
        }
        window.Hide();
        window.Width = originalWidth;
        window.ShowActivated = true;
        Console.WriteLine($"Caption: both rows, statistics and logo are HTCAPTION; controls and timeline are HTCLIENT at normal/minimum width; actual DPI {VisualTreeHelper.GetDpi(window).PixelsPerInchX}.");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
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
        using var viewModel = new MainViewModel(recorder, new DailyReportService(store, options: options));
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
                if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException($"Live view did not update automatically: date={viewModel.SelectedDate}, rows={viewModel.Activities.Count}, notice={viewModel.Notice}");
                await Task.Delay(25);
            }
        }
        await recorder.StartAsync();
        var window = new WindowInfo(101, 42, "browser", "Live tab", "https://example.com/one", ProcessStartTicks: 100);
        source.Publish(new(SignalKind.SessionActive, at));
        source.Publish(new(SignalKind.ForegroundChanged, at, window with { Url = null }));
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(1)));
        await Until(() => viewModel.Activities.Count == 1);
        var existing = viewModel.Activities[0];
        var existingProcess = viewModel.DetailApplications.Single().Children.Single();
        var existingDetail = existingProcess.Children.Single();
        Require(existing.End == "-" && existing.Duration == "-", "An open activity displays a final end or duration");
        source.Publish(new(SignalKind.ForegroundChanged, at.AddMilliseconds(1500), window));
        await Until(() => existing.Url == window.Url);
        Require(viewModel.Activities.Count == 1 && ReferenceEquals(existing, viewModel.Activities[0]),
            "Late URL resolution created a duplicate row");
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(2)));
        await Until(() => existing.Interval.Duration == TimeSpan.FromSeconds(2));
        Require(ReferenceEquals(existing, viewModel.Activities[0]), "Live duration replaced an existing row");
        Require(ReferenceEquals(existingDetail, viewModel.DetailApplications.Single().Children.Single().Children.Single()),
            "Live duration replaced an existing detail node");
        source.Publish(new(SignalKind.ForegroundChanged, at.AddSeconds(3), window with { Url = "https://example.com/two" }));
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(4)));
        await Until(() => viewModel.Activities.Count == 2);
        Require(viewModel.Activities[0].Url.EndsWith("/two", StringComparison.Ordinal), "A new URL did not appear live");
        Require(ReferenceEquals(existing, viewModel.Activities[1]), "Adding a URL replaced the previous row");
        Require(ReferenceEquals(viewModel.SelectedActivity, existing)
            && viewModel.DetailApplications.Single().Children.Single().Children.Count == 2,
            "Live insertion lost selection or did not update process details");
        Require(existing.End != "-" && existing.Duration != "-", "A closed activity did not display final times");
        Require(viewModel.Activities[0].End == "-" && viewModel.Activities[0].Duration == "-",
            "The new open URL displays a final end or duration");
        await recorder.StopAsync();
        await Until(() => viewModel.Activities.All(row => !row.Interval.IsOpen));
        Require(!viewModel.PreviousDayCommand.CanExecute(null) && !viewModel.NextDayCommand.CanExecute(null),
            "An empty history outside today enabled navigation");
        var older = existing.Interval with
        {
            Id = Guid.NewGuid(), RecordingId = Guid.NewGuid(), IsOpen = false,
            Start = at.AddDays(-4), End = at.AddDays(-4).AddMinutes(1),
            Spans = [new(Guid.NewGuid(), at.AddDays(-4), at.AddDays(-4).AddMinutes(1))]
        };
        var process = new ProcessActivity(Guid.NewGuid(), 42, "browser", null,
            older.Start, older.End, false);
        var parent = new WindowActivity(Guid.NewGuid(), process.Id, 101, "History", older.Start, older.End, false);
        older = older with { ProcessActivityId = process.Id, WindowActivityId = parent.Id };
        await store.SaveAsync(new([], [older]) { Processes = [process], Windows = [parent] });
        await viewModel.RefreshAsync();
        var dateClock = new AdjustableClock(at);
        using (var dateViewModel = new MainViewModel(recorder, new DailyReportService(store, options: options), dateClock))
        {
            await dateViewModel.RefreshAsync();
            Require(dateViewModel.SelectedDate == at.Date && dateViewModel.Activities.Count == 2,
                "Startup did not select the current local date");
            dateViewModel.PreviousDayCommand.Execute(null);
            await Until(() => dateViewModel.Activities.Count == 1);
            await dateViewModel.RefreshAsync();
            Require(dateViewModel.SelectedDate == older.Start.LocalDateTime.Date,
                "A same-day refresh discarded the selected history date");
            dateClock.Now = at.AddDays(1);
            await Until(() => dateViewModel.SelectedDate == dateClock.GetLocalNow().Date
                && dateViewModel.Activities.Count == 0);
            Require(dateViewModel.AvailableDates.Contains(dateClock.GetLocalNow().Date)
                && !dateViewModel.NextDayCommand.CanExecute(null) && !recorder.IsRecording,
                "Day rollover did not enable and select an empty today while paused");
            dateViewModel.PreviousDayCommand.Execute(null);
            await Until(() => dateViewModel.Activities.Count == 2);
            dateClock.Now = at.AddDays(4);
            // A records refresh may observe the new day before the day-change timer.
            await dateViewModel.RefreshAsync();
            Require(dateViewModel.SelectedDate == dateClock.GetLocalNow().Date && dateViewModel.Activities.Count == 0,
                "A refresh after several days did not switch history to the current date");
        }
        var picker = new System.Windows.Controls.DatePicker();
        System.Windows.Data.BindingOperations.SetBinding(picker, System.Windows.Controls.DatePicker.SelectedDateProperty,
            new System.Windows.Data.Binding(nameof(MainViewModel.SelectedDate)) { Source = viewModel });
        System.Windows.Data.BindingOperations.SetBinding(picker, ActivityDatePicker.AvailableDatesProperty,
            new System.Windows.Data.Binding(nameof(MainViewModel.AvailableDates)) { Source = viewModel });
        Require(picker.BlackoutDates.Contains(DateTime.Today.AddDays(-1))
            && picker.BlackoutDates.Contains(DateTime.Today.AddDays(1))
            && !picker.BlackoutDates.Contains(DateTime.Today)
            && !picker.BlackoutDates.Contains(at.Date.AddDays(-4)), "Calendar enabled an empty day or disabled an available day");
        viewModel.PreviousDayCommand.Execute(null);
        await Until(() => viewModel.Activities.Count == 1);
        Require(viewModel.SelectedDate == at.Date.AddDays(-4) && !viewModel.PreviousDayCommand.CanExecute(null)
            && viewModel.NextDayCommand.CanExecute(null), "Previous navigation did not skip empty dates or disable at the boundary");
        viewModel.SelectedDate = DateTime.Today.AddDays(-1);
        Require(viewModel.SelectedDate == at.Date.AddDays(-4), "An empty date was accepted");
        viewModel.NextDayCommand.Execute(null);
        await Until(() => viewModel.Activities.Count == 2);
        Require(viewModel.SelectedDate == DateTime.Today && !viewModel.NextDayCommand.CanExecute(null),
            "Next navigation did not return to today");
        viewModel.PreviousDayCommand.Execute(null);
        await Until(() => viewModel.Activities.Count == 1);
        await store.DeleteBeforeAsync(at.AddDays(-3));
        viewModel.RequestRefresh();
        await Until(() => viewModel.SelectedDate == DateTime.Today && viewModel.Activities.Count == 2);
        Require(!viewModel.PreviousDayCommand.CanExecute(null) && picker.BlackoutDates.Contains(at.Date.AddDays(-4))
            && picker.SelectedDate == DateTime.Today,
            "Retention left a deleted date available");
        var beforeRestartProcess = viewModel.DetailApplications.Single().Children.Single();
        await recorder.StartAsync();
        source.Publish(new(SignalKind.SessionActive, at.AddSeconds(10)));
        source.Publish(new(SignalKind.ForegroundChanged, at.AddSeconds(10), window));
        source.Publish(new(SignalKind.Heartbeat, at.AddSeconds(12)));
        await recorder.StopAsync();
        await Until(() => viewModel.Activities.Count == 3);
        Require(ReferenceEquals(beforeRestartProcess, viewModel.DetailApplications.Single().Children.Single())
            && beforeRestartProcess.Id == existingProcess.Id && beforeRestartProcess.Children.Count == 3,
            "Restarting recording duplicated the process detail node or lost its activities");
        Require(resets == 0, "Live updates reset the collection and lost row selection");
        Console.WriteLine("Live view: automatic insert, duration update, URL change, recording restart with one process node and date navigation passed without collection resets.");
        await CheckDefaultActivityRulesAsync(directory);
    }

    private static async Task CheckDefaultActivityRulesAsync(string directory)
    {
        var store = new SqliteActivityStore(Path.Combine(directory, $"thresholds-{Guid.NewGuid():N}.db"));
        var source = new ManualSource();
        var at = new DateTimeOffset(DateTime.Today.AddHours(12));
        await using var recorder = new Recorder(store, source, new FixedClock(at));
        using var vm = new MainViewModel(recorder, new DailyReportService(store));
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
        var icon = new ApplicationIconReader().Read(new(0, Environment.ProcessId, "editor", "Preview"));
        Require(icon is not null, "Missing sample application icon");
        await store.SaveAsync(new([], []) { ApplicationIcons = [icon!] });
        await vm.RefreshAsync();
        Require(ReferenceEquals(row, vm.Activities[0]) && row.Icon is { IsFrozen: true }, "An icon update replaced the live row or did not display");
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

    private sealed class AdjustableClock(DateTimeOffset at) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = at;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
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
