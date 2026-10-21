using SolidRecords.Core.Localization;

namespace SolidRecords.Core;

public enum ApplicationLanguage { System, English, Slovak }

public enum ApplicationTheme { System, Light, Dark }

public sealed record ApplicationSettings(int MinimumActivitySeconds = 15, bool RecordOnLaunch = true,
    bool StartWithWindows = true, bool StartMinimized = true, ApplicationTheme Theme = ApplicationTheme.System, bool CheckForUpdates = true,
    int WorkStartMinutes = 420, int WorkEndMinutes = 900, WorkDays WorkingDays = WorkDays.Weekdays,
    bool RecordOnlyDuringWorkHours = false, int? ActivityRetentionDays = null, GlobalShortcut? ToggleWindowShortcut = null, GlobalShortcut? ToggleRecordingShortcut = null,
    ApplicationLanguage Language = ApplicationLanguage.System)
{
    public void Validate()
    {
        ToggleWindowShortcut?.Validate();
        ToggleRecordingShortcut?.Validate();
        if (ToggleWindowShortcut is not null && ToggleWindowShortcut == ToggleRecordingShortcut)
            throw new ArgumentException(Strings.Get("DuplicateShortcut"));
        if (ActivityRetentionDays is <= 0) throw new ArgumentOutOfRangeException(nameof(ActivityRetentionDays));
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumActivitySeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MinimumActivitySeconds, 600);
        ArgumentOutOfRangeException.ThrowIfNegative(WorkStartMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(WorkStartMinutes, 1440);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(WorkEndMinutes, WorkStartMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(WorkEndMinutes, 1440);
        if ((WorkingDays & ~WorkDays.All) != 0) throw new ArgumentOutOfRangeException(nameof(WorkingDays));
        if (!Enum.IsDefined(Language)) throw new ArgumentOutOfRangeException(nameof(Language));
        if (!Enum.IsDefined(Theme)) throw new ArgumentOutOfRangeException(nameof(Theme));
    }
}

public interface ISettingsStore
{
    Task<ApplicationSettings?> ReadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
