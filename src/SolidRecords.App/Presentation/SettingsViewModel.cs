using System.Globalization;
using SolidRecords.Core;
using SolidRecords.Core.Localization;

namespace SolidRecords.App.Presentation;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly Func<ApplicationSettings, Task> _save;
    private readonly Func<Task<string>> _updateNow;
    private bool _isUpdating;
    private string _updateStatus = "";
    private string _minimumActivitySeconds;
    private string _activityRetentionDays;
    private bool _deleteOldActivities;
    private int _recordingModeIndex;
    private string _workStart;
    private string _workEnd;
    private WorkDays _workingDays;
    private bool _startWithWindows;
    private bool _startMinimized;
    private int _themeIndex;
    private int _languageIndex;
    private bool _checkForUpdates;
    private string _error = "";
    private bool _isSaving;
    private GlobalShortcut? _toggleWindowShortcut;
    private GlobalShortcut? _toggleRecordingShortcut;

    public SettingsViewModel(ApplicationSettings settings, Func<ApplicationSettings, Task> save, Func<Task<string>>? updateNow = null)
    {
        _toggleWindowShortcut = settings.ToggleWindowShortcut;
        _toggleRecordingShortcut = settings.ToggleRecordingShortcut;
        _save = save;
        _updateNow = updateNow ?? (() => Task.FromResult(Strings.Get("PreviewUpdates")));
        UpdateNowCommand = new(UpdateNowAsync, ex => UpdateStatus = Strings.Get("UpdateFailed") + ex.Message);
        _deleteOldActivities = settings.ActivityRetentionDays.HasValue;
        _activityRetentionDays = settings.ActivityRetentionDays?.ToString() ?? "30";
        _minimumActivitySeconds = settings.MinimumActivitySeconds.ToString();
        _recordingModeIndex = !settings.RecordOnLaunch ? 0 : settings.RecordOnlyDuringWorkHours ? 2 : 1;
        _workStart = FormatTime(settings.WorkStartMinutes);
        _workEnd = FormatTime(settings.WorkEndMinutes);
        _workingDays = settings.WorkingDays;
        _startWithWindows = settings.StartWithWindows;
        _startMinimized = settings.StartMinimized;
        _themeIndex = (int)settings.Theme;
        _languageIndex = (int)settings.Language;
        _checkForUpdates = settings.CheckForUpdates;
        SaveCommand = new(SaveAsync, ex => Error = Strings.Get("SaveFailed") + ex.Message);
    }

    public GlobalShortcut? ToggleWindowShortcut { get => _toggleWindowShortcut; set => Set(ref _toggleWindowShortcut, value); }
    public GlobalShortcut? ToggleRecordingShortcut { get => _toggleRecordingShortcut; set => Set(ref _toggleRecordingShortcut, value); }

    public bool DeleteOldActivities { get => _deleteOldActivities; set => Set(ref _deleteOldActivities, value); }
    public string ActivityRetentionDays { get => _activityRetentionDays; set => Set(ref _activityRetentionDays, value); }
    public string MinimumActivitySeconds { get => _minimumActivitySeconds; set => Set(ref _minimumActivitySeconds, value); }
    private static string FormatTime(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
    public int RecordingModeIndex { get => _recordingModeIndex; set => Set(ref _recordingModeIndex, value); }
    public string WorkStart { get => _workStart; set => Set(ref _workStart, value); }
    public string WorkEnd { get => _workEnd; set => Set(ref _workEnd, value); }
    private bool HasDay(WorkDays day) => (_workingDays & day) != 0;
    private void SetDay(WorkDays day, bool value, string property)
    {
        var days = value ? _workingDays | day : _workingDays & ~day;
        if (days == _workingDays) return;
        _workingDays = days;
        Changed(property);
    }
    public bool Monday { get => HasDay(WorkDays.Monday); set => SetDay(WorkDays.Monday, value, nameof(Monday)); }
    public bool Tuesday { get => HasDay(WorkDays.Tuesday); set => SetDay(WorkDays.Tuesday, value, nameof(Tuesday)); }
    public bool Wednesday { get => HasDay(WorkDays.Wednesday); set => SetDay(WorkDays.Wednesday, value, nameof(Wednesday)); }
    public bool Thursday { get => HasDay(WorkDays.Thursday); set => SetDay(WorkDays.Thursday, value, nameof(Thursday)); }
    public bool Friday { get => HasDay(WorkDays.Friday); set => SetDay(WorkDays.Friday, value, nameof(Friday)); }
    public bool Saturday { get => HasDay(WorkDays.Saturday); set => SetDay(WorkDays.Saturday, value, nameof(Saturday)); }
    public bool Sunday { get => HasDay(WorkDays.Sunday); set => SetDay(WorkDays.Sunday, value, nameof(Sunday)); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }
    public bool CheckForUpdates { get => _checkForUpdates; set => Set(ref _checkForUpdates, value); }
    public int LanguageIndex { get => _languageIndex; set => Set(ref _languageIndex, value); }
    public int ThemeIndex { get => _themeIndex; set => Set(ref _themeIndex, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public bool IsSaving { get => _isSaving; private set { if (Set(ref _isSaving, value)) Changed(nameof(IsEditable)); } }
    public bool IsEditable => !IsSaving && !IsUpdating;
    public bool IsUpdating { get => _isUpdating; private set { if (Set(ref _isUpdating, value)) Changed(nameof(IsEditable)); } }
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }
    public AsyncCommand UpdateNowCommand { get; }

    private async Task UpdateNowAsync()
    {
        IsUpdating = true;
        UpdateStatus = Strings.Get("CheckingUpdates");
        try { UpdateStatus = await _updateNow(); }
        finally { IsUpdating = false; }
    }

    public AsyncCommand SaveCommand { get; }
    public event EventHandler? Saved;

    private async Task SaveAsync()
    {
        Error = "";
        if (!int.TryParse(MinimumActivitySeconds, out var seconds) || seconds is < 0 or > 600)
        {
            Error = Strings.Get("InvalidSeconds");
            return;
        }
        int? retentionDays = null;
        if (DeleteOldActivities)
        {
            if (!int.TryParse(ActivityRetentionDays, out var days) || days <= 0)
            {
                Error = Strings.Get("InvalidDays");
                return;
            }
            retentionDays = days;
        }
        static int? ParseTime(string text)
        {
            if (text.Trim() == "24:00") return 1440;
            return TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time) ? time.Hour * 60 + time.Minute : null;
        }
        var start = ParseTime(WorkStart);
        var end = ParseTime(WorkEnd);
        if (start is null or >= 1440 || end is null || end <= start)
        {
            Error = Strings.Get("InvalidWorkHours");
            return;
        }
        if (RecordingModeIndex is < 0 or > 2)
        {
            Error = Strings.Get("SelectRecordingMode");
            return;
        }
        var settings = new ApplicationSettings(seconds, RecordingModeIndex != 0, StartWithWindows, StartMinimized,
            (ApplicationTheme)ThemeIndex, CheckForUpdates, start.Value, end.Value, _workingDays, RecordingModeIndex == 2, retentionDays, ToggleWindowShortcut, ToggleRecordingShortcut, (ApplicationLanguage)LanguageIndex);
        settings.Validate();
        IsSaving = true;
        try { await _save(settings); }
        finally { IsSaving = false; }
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
