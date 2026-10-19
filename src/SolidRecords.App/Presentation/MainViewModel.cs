using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SolidRecords.Core;
using SolidRecords.Core.Recording;
using SolidRecords.Core.Reporting;

namespace SolidRecords.App.Presentation;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private Dictionary<string, ImageSource?> _icons = [];
    private readonly Recorder _recorder;
    private readonly DailyReportService _reports;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _dayChange;
    private DateTime _today = DateTime.Today;
    private IReadOnlyList<DateTime> _availableDates = [DateTime.Today];
    private DateTime? _selectedDate = DateTime.Today;
    private string _search = "";
    private string _error = "";
    private DailyReport _report = new([]);
    private ApplicationSettings _settings = new();
    private bool _detailsVisible;
    private bool _updatingRows;
    private ActivityRow? _selectedActivity;
    private bool _loading;
    private bool _reloadRequested;
    private volatile bool _disposed;
    private int _refreshPending;

    public MainViewModel(Recorder recorder, DailyReportService reports)
    {
        _recorder = recorder;
        _reports = reports;
        _dispatcher = Dispatcher.CurrentDispatcher;
        ToggleRecordingCommand = new(ToggleRecordingAsync, ShowError);
        PreviousDayCommand = new(() => SelectedDate = AdjacentDate(-1), () => AdjacentDate(-1) is not null);
        NextDayCommand = new(() => SelectedDate = AdjacentDate(1), () => AdjacentDate(1) is not null);
        TodayCommand = new(() => SelectedDate = DateTime.Today);
        _recorder.StateChanged += RecorderChanged;
        _recorder.RecordsChanged += RecordsChanged;
        // Coalesce bursts of window events; a continuous event stream cannot postpone a refresh.
        _refresh = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _refresh.Tick += RefreshTick;
        _dayChange = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dayChange.Tick += DayChangeTick;
        _dayChange.Start();
    }

    public AsyncCommand ToggleRecordingCommand { get; }
    public RelayCommand PreviousDayCommand { get; }
    public RelayCommand NextDayCommand { get; }
    public RelayCommand TodayCommand { get; }
    public ObservableCollection<ActivityRow> Activities { get; } = [];
    public ObservableCollection<IActivityListRow> EventRows { get; } = [];
    public ObservableCollection<ActivityDetailNode> DetailApplications { get; } = [];
    public bool DetailsVisible { get => _detailsVisible; set => Set(ref _detailsVisible, value); }
    public ActivityRow? SelectedActivity
    {
        get => _selectedActivity;
        set
        {
            if (!Set(ref _selectedActivity, value)) return;
            Changed(nameof(SelectedEventRow));
            if (!_updatingRows) UpdateDetails();
        }
    }
    public object? SelectedEventRow
    {
        get => SelectedActivity;
        set { if (value is null or ActivityRow) SelectedActivity = value as ActivityRow; }
    }
    public IReadOnlyList<ActivityInterval> Timeline => _report.Intervals;
    public WorkPeriod TimelineBounds => TimelineRange.Calculate(DateOnly.FromDateTime(_selectedDate ?? DateTime.Today), _settings, Timeline);
    public void ApplySettings(ApplicationSettings settings)
    {
        _settings = settings;
        Changed(nameof(TimelineBounds));
    }
    public string ToggleLabel => _recorder.IsRecording ? "■  Zastaviť záznam" : "▶  Spustiť záznam";
    public string Status => _recorder.Error is not null ? "Zápis zlyhal" : _recorder.IsRecording ? "Zaznamenávanie je aktívne" : "Zaznamenávanie je pozastavené";
    public string StatusColor => _recorder.Error is not null ? "#D63D4F" : _recorder.IsRecording ? "#16A078" : "#8A96AA";
    public string Notice => _error.Length > 0 ? _error : "Údaje zostávajú v tomto počítači. Zatvorením okna aplikácia pokračuje v systray.";
    public string Started => _report.FirstActivity?.ToLocalTime().ToString("HH:mm") ?? "—";
    public string Finished => _report.LastActivity?.ToLocalTime().ToString("HH:mm") ?? "—";
    public string TotalDuration => ActivityRow.FormatDuration(_report.ActiveDuration);
    public string Count => _report.Intervals.Count.ToString();
    public string Details => $"{Activities.Count} aktivít  ·  {_report.Processes.Count} procesov  ·  časy v miestnom časovom pásme";
    public bool IsEmpty => EventRows.Count == 0;
    public IReadOnlyList<DateTime> AvailableDates => _availableDates;
    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (value is not { } date || !_availableDates.Contains(date.Date)) return;
            if (!Set(ref _selectedDate, date.Date)) return;
            NotifyNavigation();
            _ = RefreshSafelyAsync();
        }
    }
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) FilterRows(); }
    }

    public async Task ToggleRecordingAsync()
    {
        _error = "";
        if (_recorder.IsRecording) await _recorder.StopAsync();
        else await _recorder.StartAsync();
        NotifyState();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _reloadRequested = true;
        if (_loading) return;
        _loading = true;
        try
        {
            do
            {
                _reloadRequested = false;
                var date = (_selectedDate ?? DateTime.Today).Date;
                // SQLite executes much of its work synchronously. Keep reads off the UI thread.
                var icons = new Dictionary<string, ImageSource?>(_icons);
                var report = await Task.Run(async () =>
                {
                    var result = await _reports.ReadAsync(DateOnly.FromDateTime(date));
                    foreach (var png in result.Intervals.Select(x => x.ApplicationIconPngBase64).OfType<string>().Distinct())
                    {
                        if (icons.ContainsKey(png)) continue;
                        ImageSource? image = null;
                        try
                        {
                            using var stream = new MemoryStream(Convert.FromBase64String(png));
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 32;
                            bitmap.StreamSource = stream;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            image = bitmap;
                        }
                        catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException or ArgumentException or InvalidOperationException) { }
                        icons[png] = image;
                    }
                    return (Report: result, Dates: await _reports.ReadActivityDatesAsync());
                });
                if (_disposed) return;
                UpdateAvailableDates(report.Dates);
                if (date != (_selectedDate ?? DateTime.Today).Date) { _reloadRequested = true; continue; }
                _icons = icons;
                _report = report.Report;
                FilterRows();
                foreach (var name in new[] { nameof(Timeline), nameof(TimelineBounds), nameof(Started), nameof(Finished), nameof(TotalDuration), nameof(Count) }) Changed(name);
            } while (_reloadRequested && !_disposed);
        }
        finally { _loading = false; }
    }

    private DateTime? AdjacentDate(int direction) => direction < 0
        ? _availableDates.Where(date => date < _selectedDate).Select(date => (DateTime?)date).LastOrDefault()
        : _availableDates.Where(date => date > _selectedDate).Select(date => (DateTime?)date).FirstOrDefault();

    private void NotifyNavigation()
    {
        PreviousDayCommand.NotifyCanExecuteChanged();
        NextDayCommand.NotifyCanExecuteChanged();
    }

    private void UpdateAvailableDates(IReadOnlyList<DateOnly> dates)
    {
        _today = DateTime.Today;
        var available = dates.Select(date => date.ToDateTime(TimeOnly.MinValue)).Append(_today).Distinct().Order().ToArray();
        if (!_availableDates.SequenceEqual(available))
        {
            _availableDates = available;
            Changed(nameof(AvailableDates));
        }
        if (!_availableDates.Contains(_selectedDate ?? _today)) SelectedDate = _today;
        NotifyNavigation();
    }

    private void DayChangeTick(object? sender, EventArgs e)
    {
        if (_today != DateTime.Today) RequestRefresh();
    }

    private async Task RefreshSafelyAsync()
    {
        try { await RefreshAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void FilterRows()
    {
        bool Matches(params string?[] values) => string.IsNullOrWhiteSpace(_search) || values.Any(v =>
            v?.Contains(_search, StringComparison.CurrentCultureIgnoreCase) == true);
        var desired = _report.Intervals.Reverse()
            .Where(interval => Matches(interval.Window.Application, interval.Window.Title, interval.Window.Url)).ToArray();
        _updatingRows = true;
        var selectionId = SelectedActivity?.Interval.Id;
        var ids = desired.Select(interval => interval.Id).ToHashSet();
        for (var i = Activities.Count - 1; i >= 0; i--)
            if (!ids.Contains(Activities[i].Interval.Id)) Activities.RemoveAt(i);
        var existing = Activities.ToDictionary(row => row.Interval.Id);
        for (var i = 0; i < desired.Length; i++)
        {
            var interval = desired[i];
            if (!existing.TryGetValue(interval.Id, out var row))
            {
                row = new(interval);
                row.UpdateIcon(GetIcon(interval));
                Activities.Insert(i, row);
                continue;
            }
            row.Update(interval);
            row.UpdateIcon(GetIcon(interval));
            if (!ReferenceEquals(Activities[i], row)) Activities.Move(Activities.IndexOf(row), i);
        }
        SynchronizeEventRows();
        _updatingRows = false;
        SelectedActivity = Activities.FirstOrDefault(row => row.Interval.Id == selectionId) ?? Activities.FirstOrDefault();
        UpdateDetails();
        Changed(nameof(Details));
        Changed(nameof(IsEmpty));
    }

    private void SynchronizeEventRows()
    {
        var existing = EventRows.OfType<SessionEventRow>().ToDictionary(row => row.Id);
        var separators = _report.SessionEvents.Select(entry =>
        {
            if (!existing.TryGetValue(entry.Id, out var row)) return new SessionEventRow(entry);
            row.Update(entry);
            return row;
        });
        var desired = Activities.Cast<IActivityListRow>().Concat(separators)
            .OrderByDescending(row => row.Timestamp)
            .ThenBy(row => row is SessionEventRow session ? session.IsActive ? 2 : 0 : 1).ToArray();
        var ids = desired.Select(row => row.Id).ToHashSet();
        for (var i = EventRows.Count - 1; i >= 0; i--)
            if (!ids.Contains(EventRows[i].Id)) EventRows.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++)
        {
            var index = EventRows.IndexOf(desired[i]);
            if (index < 0) EventRows.Insert(i, desired[i]);
            else if (index != i) EventRows.Move(index, i);
        }
    }

    private void UpdateDetails()
    {
        var application = SelectedActivity?.Application;
        var processes = _report.Processes
            .Where(process => string.Equals(process.Application, application, StringComparison.OrdinalIgnoreCase))
            .OrderBy(process => process.Start).ToArray();
        ActivityDetailNode.Synchronize(DetailApplications, application is null ? [] : [application], (app, _) =>
        {
            var intervals = processes.SelectMany(process => process.Windows).SelectMany(window => window.Activities).ToArray();
            app.Update(application!, $"{processes.Length} procesov · {intervals.Length} aktivít", application!, SelectedActivity?.Icon);
            ActivityDetailNode.Synchronize(app.Children, processes.Select(process => process.Id.ToString()).ToArray(), (node, index) =>
            {
                var process = processes[index];
                var activities = process.Windows.SelectMany(window => window.Activities).OrderBy(activity => activity.Start).ToArray();
                node.Update($"PID {process.ProcessId}",
                    $"{activities.Length} aktivít · {ActivityRow.FormatDuration(TimeSpan.FromTicks(activities.Sum(activity => activity.Duration.Ticks)))}",
                    $"Pozorovaný proces od {process.Start.ToLocalTime():g}");
                ActivityDetailNode.Synchronize(node.Children, activities.Select(activity => activity.Id.ToString()).ToArray(), (leaf, activityIndex) =>
                {
                    var row = new ActivityRow(activities[activityIndex]);
                    leaf.Update(row.Description, $"{row.Start} – {row.End} · {row.Duration}",
                        $"{row.Description}\n{row.Url}\n{row.Start} – {row.End} · {row.Duration}");
                });
            });
        });
    }

    private ImageSource? GetIcon(ActivityInterval interval) => interval.ApplicationIconPngBase64 is { } png
        && _icons.TryGetValue(png, out var icon) ? icon : null;

    public void ShowError(Exception exception)
    {
        _error = "Chyba: " + exception.Message;
        Changed(nameof(Notice));
    }

    public void RequestRefresh()
    {
        if (!_disposed && !_refresh.IsEnabled) _refresh.Start();
    }

    private void RefreshTick(object? sender, EventArgs e)
    {
        _refresh.Stop();
        _ = RefreshSafelyAsync();
    }

    private void RecordsChanged(object? sender, EventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        _dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            RequestRefresh();
        });
    }

    private void RecorderChanged(object? sender, EventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() => { if (!_disposed) NotifyState(); });
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(ToggleLabel), nameof(Status), nameof(StatusColor), nameof(Notice) }) Changed(name);
    }

    public void Dispose()
    {
        _disposed = true;
        _refresh.Stop();
        _refresh.Tick -= RefreshTick;
        _dayChange.Stop();
        _dayChange.Tick -= DayChangeTick;
        _recorder.StateChanged -= RecorderChanged;
        _recorder.RecordsChanged -= RecordsChanged;
    }
}
