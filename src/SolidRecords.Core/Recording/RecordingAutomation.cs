namespace SolidRecords.Core.Recording;

/// <summary>Automatic transitions; manual toggles remain effective until the next schedule boundary.</summary>
public sealed class RecordingAutomation
{
    private ApplicationSettings? _previous;
    private WorkPeriod? _period;
    private bool _working;

    public bool? Evaluate(ApplicationSettings settings, DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var period = WorkSchedule.GetPeriod(settings, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime), zone);
        var working = period is not null && now >= period.Start && now < period.End;
        var enabled = settings.RecordOnLaunch && settings.RecordOnlyDuringWorkHours;
        var wasEnabled = _previous is { RecordOnLaunch: true, RecordOnlyDuringWorkHours: true };
        var changed = _previous is null || enabled != wasEnabled
            || (enabled && (working != _working || (working && period != _period)
                || settings.WorkStartMinutes != _previous.WorkStartMinutes
                || settings.WorkEndMinutes != _previous.WorkEndMinutes || settings.WorkingDays != _previous.WorkingDays));
        var initial = _previous is null;
        _previous = settings;
        _working = working;
        _period = period;
        if (!changed) return null;
        if (enabled) return working;
        return initial || wasEnabled ? settings.RecordOnLaunch : null;
    }
}
