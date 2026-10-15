using System.Collections.ObjectModel;
using System.Windows.Threading;
using SolidRecords.Core;

namespace SolidRecords.App.Presentation;

public sealed class MainViewModel : ObservableObject, IDisposable
{
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

    public MainViewModel(Recorder recorder, DailyReportService reports, Func<bool, Task> savePreference)
    {
        _recorder = recorder;
        _reports = reports;
        _savePreference = savePreference;
        _dispatcher = Dispatcher.CurrentDispatcher;
        ToggleRecordingCommand = new(ToggleRecordingAsync, ShowError);
        RefreshCommand = new(RefreshAsync, ShowError);
        PreviousDayCommand = new(() => SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(-1));
        NextDayCommand = new(() => SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(1));
        TodayCommand = new(() => SelectedDate = DateTime.Today);
        _recorder.StateChanged += RecorderChanged;
        _refresh = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            (_, _) => RefreshCommand.Execute(null), _dispatcher);
    }

    public AsyncCommand ToggleRecordingCommand { get; }
    public AsyncCommand RefreshCommand { get; }
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
                var report = await Task.Run(() => _reports.ReadAsync(DateOnly.FromDateTime(date)));
                if (date != (_selectedDate ?? DateTime.Today).Date) { _reloadRequested = true; continue; }
                _report = report;
                FilterRows();
                foreach (var name in new[] { nameof(Timeline), nameof(Started), nameof(Finished), nameof(TotalDuration), nameof(Count) }) Changed(name);
            } while (_reloadRequested);
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
        Activities.Clear();
        foreach (var interval in _report.Intervals.Reverse())
            if (Matches(interval.Window.Application, interval.Window.Title, interval.Window.Url)) Activities.Add(new(interval));
        Changed(nameof(Details));
        Changed(nameof(IsEmpty));
    }

    public void ShowError(Exception exception)
    {
        _error = "Chyba: " + exception.Message;
        Changed(nameof(Notice));
    }

    private void RecorderChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(NotifyState);
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(ToggleLabel), nameof(Status), nameof(StatusColor), nameof(Notice) }) Changed(name);
    }

    public void Dispose()
    {
        _refresh.Stop();
        _recorder.StateChanged -= RecorderChanged;
    }
}
