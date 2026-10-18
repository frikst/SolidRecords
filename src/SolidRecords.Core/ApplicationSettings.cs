namespace SolidRecords.Core;

public enum ApplicationTheme { System, Light, Dark }

public sealed record ApplicationSettings(int MinimumActivitySeconds = 15, bool RecordOnLaunch = true,
    bool StartWithWindows = true, bool StartMinimized = true, ApplicationTheme Theme = ApplicationTheme.System, bool CheckForUpdates = true)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumActivitySeconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MinimumActivitySeconds, 600);
        if (!Enum.IsDefined(Theme)) throw new ArgumentOutOfRangeException(nameof(Theme));
    }
}

public interface ISettingsStore
{
    Task<ApplicationSettings?> ReadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
