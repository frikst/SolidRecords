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
    private readonly Func<bool, Task> _savePreference;
    private DateTime? _selectedDate = DateTime.Today;
    private string _search = "";
    private string _error = "";
    private DailyReport _report = new([]);
    private bool _loading;
    private bool _reloadRequested;
    private volatile bool _disposed;
    private int _refreshPending;

    public MainViewModel(Recorder recorder, DailyReportService reports, Func<bool, Task> savePreference)
    {
        _recorder = recorder;
        _reports = reports;
        _savePreference = savePreference;
        _dispatcher = Dispatcher.CurrentDispatcher;
        ToggleRecordingCommand = new(ToggleRecordingAsync, ShowError);
        PreviousDayCommand = new(() => SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(-1));
        NextDayCommand = new(() => SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(1));
        TodayCommand = new(() => SelectedDate = DateTime.Today);
        _recorder.StateChanged += RecorderChanged;
        _recorder.RecordsChanged += RecordsChanged;
        // Coalesce bursts of window events; a continuous event stream cannot postpone a refresh.
        _refresh = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _refresh.Tick += RefreshTick;
    }

    public AsyncCommand ToggleRecordingCommand { get; }
    public RelayCommand PreviousDayCommand { get; }
    public RelayCommand NextDayCommand { get; }
    public RelayCommand TodayCommand { get; }
    public ObservableCollection<ActivityRow> Activities { get; } = [];
    public IReadOnlyList<ActivityInterval> Timeline => _report.Intervals;
    public string ToggleLabel => _recorder.IsRecording ? "■  Zastaviť záznam" : "▶  Spustiť záznam";
    public string Status => _recorder.Error is not null ? "Zápis zlyhal" : _recorder.IsRecording ? "Zaznamenávanie je aktívne" : "Zaznamenávanie je pozastavené";
    public string StatusColor => _recorder.Error is not null ? "#D63D4F" : _recorder.IsRecording ? "#16A078" : "#8A96AA";
    public string Notice => _error.Length > 0 ? _error : "Údaje zostávajú v tomto počítači. Zatvorením okna aplikácia pokračuje v systray.";
    public string Started => _report.FirstActivity?.ToLocalTime().ToString("HH:mm") ?? "—";
    public string Finished => _report.LastActivity?.ToLocalTime().ToString("HH:mm") ?? "—";
    public string TotalDuration => ActivityRow.FormatDuration(_report.ActiveDuration);
    public string Count => _report.Intervals.Count.ToString();
    public string Details => $"{Activities.Count} aktivít  ·  {_report.Processes.Count} procesov  ·  časy v miestnom časovom pásme";
    public bool IsEmpty => Activities.Count == 0;
    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set { if (Set(ref _selectedDate, value)) _ = RefreshSafelyAsync(); }
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
        await _savePreference(_recorder.IsRecording);
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
                    return result;
                });
                if (_disposed) return;
                if (date != (_selectedDate ?? DateTime.Today).Date) { _reloadRequested = true; continue; }
                _icons = icons;
                _report = report;
                FilterRows();
                foreach (var name in new[] { nameof(Timeline), nameof(Started), nameof(Finished), nameof(TotalDuration), nameof(Count) }) Changed(name);
            } while (_reloadRequested && !_disposed);
        }
        finally { _loading = false; }
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
        Changed(nameof(Details));
        Changed(nameof(IsEmpty));
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
        _recorder.StateChanged -= RecorderChanged;
        _recorder.RecordsChanged -= RecordsChanged;
    }
}
