using SolidRecords.Core;

namespace SolidRecords.App.Presentation;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly Func<ApplicationSettings, Task> _save;
    private string _minimumActivitySeconds;
    private bool _recordOnLaunch;
    private bool _startWithWindows;
    private bool _startMinimized;
    private int _themeIndex;
    private bool _checkForUpdates;
    private string _error = "";
    private bool _isSaving;

    public SettingsViewModel(ApplicationSettings settings, Func<ApplicationSettings, Task> save)
    {
        _save = save;
        _minimumActivitySeconds = settings.MinimumActivitySeconds.ToString();
        _recordOnLaunch = settings.RecordOnLaunch;
        _startWithWindows = settings.StartWithWindows;
        _startMinimized = settings.StartMinimized;
        _themeIndex = (int)settings.Theme;
        _checkForUpdates = settings.CheckForUpdates;
        SaveCommand = new(SaveAsync, ex => Error = "Nastavenia sa nepodarilo uložiť: " + ex.Message);
    }

    public string MinimumActivitySeconds { get => _minimumActivitySeconds; set => Set(ref _minimumActivitySeconds, value); }
    public bool RecordOnLaunch { get => _recordOnLaunch; set => Set(ref _recordOnLaunch, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }
    public bool CheckForUpdates { get => _checkForUpdates; set => Set(ref _checkForUpdates, value); }
    public int ThemeIndex { get => _themeIndex; set => Set(ref _themeIndex, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public bool IsSaving { get => _isSaving; private set { if (Set(ref _isSaving, value)) Changed(nameof(IsEditable)); } }
    public bool IsEditable => !IsSaving;
    public AsyncCommand SaveCommand { get; }
    public event EventHandler? Saved;

    private async Task SaveAsync()
    {
        Error = "";
        if (!int.TryParse(MinimumActivitySeconds, out var seconds) || seconds is < 0 or > 600)
        {
            Error = "Zadajte celé číslo od 0 do 600 sekúnd.";
            return;
        }
        var settings = new ApplicationSettings(seconds, RecordOnLaunch, StartWithWindows, StartMinimized, (ApplicationTheme)ThemeIndex, CheckForUpdates);
        settings.Validate();
        IsSaving = true;
        try { await _save(settings); }
        finally { IsSaving = false; }
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
