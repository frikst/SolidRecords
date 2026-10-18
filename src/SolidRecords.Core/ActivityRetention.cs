namespace SolidRecords.Core;

public static class ActivityRetention
{
    public static DateTimeOffset? GetCutoff(ApplicationSettings settings, DateTimeOffset now)
    {
        settings.Validate();
        if (settings.ActivityRetentionDays is not int days) return null;
        var utc = now.ToUniversalTime();
        return days >= (utc - DateTimeOffset.MinValue).TotalDays
            ? DateTimeOffset.MinValue : utc.AddDays(-days);
    }
}
