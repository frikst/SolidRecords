namespace SolidRecords.Core;

public enum ApplicationTheme { System, Light, Dark }

public sealed record ApplicationSettings(int MinimumActivitySeconds = 15, bool RecordOnLaunch = true,
    bool StartWithWindows = true, bool StartMinimized = true, ApplicationTheme Theme = ApplicationTheme.System, bool CheckForUpdates = true,
    int WorkStartMinutes = 420, int WorkEndMinutes = 900, WorkDays WorkingDays = WorkDays.Weekdays,
    bool RecordOnlyDuringWorkHours = false)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumActivitySeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MinimumActivitySeconds, 600);
        ArgumentOutOfRangeException.ThrowIfNegative(WorkStartMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(WorkStartMinutes, 1440);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(WorkEndMinutes, WorkStartMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(WorkEndMinutes, 1440);
        if ((WorkingDays & ~WorkDays.All) != 0) throw new ArgumentOutOfRangeException(nameof(WorkingDays));
        if (!Enum.IsDefined(Theme)) throw new ArgumentOutOfRangeException(nameof(Theme));
    }
}

public interface ISettingsStore
{
    Task<ApplicationSettings?> ReadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
