namespace SolidRecords.Core;

[Flags]
public enum WorkDays
{
    None = 0, Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8,
    Friday = 16, Saturday = 32, Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = Weekdays | Saturday | Sunday
}

public sealed record WorkPeriod(DateTimeOffset Start, DateTimeOffset End);

public static class WorkSchedule
{
    public static WorkPeriod? GetPeriod(ApplicationSettings settings, DateOnly date, TimeZoneInfo? timeZone = null)
    {
        var day = (WorkDays)(1 << (((int)date.DayOfWeek + 6) % 7));
        if ((settings.WorkingDays & day) == 0) return null;
        var zone = timeZone ?? TimeZoneInfo.Local;
        DateTimeOffset At(int minutes)
        {
            var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
            // A missing wall-clock time starts at the first valid minute after the DST jump.
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            return TimeZoneInfo.ConvertTime(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)), zone);
        }
        var start = At(settings.WorkStartMinutes);
        var end = At(settings.WorkEndMinutes);
        return end > start ? new(start, end) : null;
    }

    public static bool IsWorkingTime(ApplicationSettings settings, DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var period = GetPeriod(settings, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime), zone);
        return period is not null && now >= period.Start && now < period.End;
    }
}
