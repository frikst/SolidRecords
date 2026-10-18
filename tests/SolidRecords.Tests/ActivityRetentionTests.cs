using System.Text.Json;
using SolidRecords.Core;
using Xunit;

namespace SolidRecords.Tests;

public sealed class ActivityRetentionTests
{
    [Fact]
    public void MissingRetentionMeansNeverAndLargeValuesAreSafe()
    {
        var old = JsonSerializer.Deserialize<ApplicationSettings>("{\"MinimumActivitySeconds\":15}")!;
        Assert.Null(old.ActivityRetentionDays);
        Assert.Null(ActivityRetention.GetCutoff(old, DateTimeOffset.UtcNow));
        Assert.Equal(DateTimeOffset.MinValue, ActivityRetention.GetCutoff(new(ActivityRetentionDays: int.MaxValue), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CutoffUsesElapsedDaysAcrossDaylightSavingChange()
    {
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(1));
        var cutoff = ActivityRetention.GetCutoff(new(ActivityRetentionDays: 1), now)!.Value;
        Assert.Equal(TimeSpan.FromHours(24), now - cutoff);
        Assert.Equal(now.UtcDateTime.AddDays(-1), cutoff.UtcDateTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidRetentionIsRejected(int days) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(ActivityRetentionDays: days).Validate());
}
