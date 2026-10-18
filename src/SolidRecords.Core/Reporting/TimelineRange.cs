namespace SolidRecords.Core.Reporting;

public static class TimelineRange
{
    public static WorkPeriod Calculate(DateOnly date, ApplicationSettings settings,
        IReadOnlyList<ActivityInterval> items, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var work = WorkSchedule.GetPeriod(settings, date, zone);
        DateTimeOffset FloorHour(DateTimeOffset time)
        {
            var local = TimeZoneInfo.ConvertTime(time, zone);
            return local.AddTicks(-(local.Ticks % TimeSpan.TicksPerHour));
        }
        DateTimeOffset from;
        DateTimeOffset to;
        if (items.Count > 0)
        {
            from = FloorHour(items.Min(x => x.Start));
            to = FloorHour(items.Max(x => x.End)).AddHours(1);
            if (work is not null)
            {
                if (work.Start < from) from = work.Start;
                if (work.End > to) to = work.End;
            }
        }
        else if (work is not null) { from = work.Start; to = work.End; }
        else
        {
            from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(6, 0)), zone));
            to = from.AddHours(12);
        }
        if (to - from < TimeSpan.FromHours(2)) to = from.AddHours(2);
        return new(TimeZoneInfo.ConvertTime(from, zone), TimeZoneInfo.ConvertTime(to, zone));
    }
}
