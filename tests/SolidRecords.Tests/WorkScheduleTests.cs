using System.Text.Json;
using SolidRecords.Core;
using SolidRecords.Core.Recording;
using SolidRecords.Core.Reporting;
using Xunit;

namespace SolidRecords.Tests;

public sealed class WorkScheduleTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");
    private static readonly ApplicationSettings Scheduled = new(RecordOnlyDuringWorkHours: true);
    private static DateTimeOffset Monday(int hour, int minute = 0) => new(2026, 10, 19, hour, minute, 0, TimeSpan.FromHours(2));

    [Fact]
    public void OldJsonReceivesDefaultsAndNewSettingsRoundTrip()
    {
        var settings = JsonSerializer.Deserialize<ApplicationSettings>("{\"RecordOnLaunch\":false}")!;
        Assert.False(settings.RecordOnLaunch);
        Assert.False(settings.RecordOnlyDuringWorkHours);
        Assert.Equal(420, settings.WorkStartMinutes);
        Assert.Equal(900, settings.WorkEndMinutes);
        Assert.Equal(WorkDays.Weekdays, settings.WorkingDays);
        settings = Scheduled with { WorkStartMinutes = 510, WorkEndMinutes = 1440, WorkingDays = WorkDays.Tuesday | WorkDays.Sunday };
        Assert.Equal(settings, JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings)));
    }

    [Theory]
    [InlineData(-1, 900)]
    [InlineData(1440, 1440)]
    [InlineData(420, 420)]
    [InlineData(900, 420)]
    [InlineData(420, 1441)]
    public void InvalidWorkHoursAreRejected(int start, int end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => (Scheduled with { WorkStartMinutes = start, WorkEndMinutes = end }).Validate());

    [Fact]
    public void EmptyWeekIsAllowedButUnknownDaysAreRejected()
    {
        (Scheduled with { WorkingDays = WorkDays.None }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (Scheduled with { WorkingDays = (WorkDays)128 }).Validate());
        Assert.Null(WorkSchedule.GetPeriod(Scheduled with { WorkingDays = WorkDays.None }, new(2026, 10, 19), Zone));
    }

    [Theory]
    [InlineData(6, 59, false)]
    [InlineData(7, 0, true)]
    [InlineData(14, 59, true)]
    [InlineData(15, 0, false)]
    public void WorkHoursIncludeStartAndExcludeEnd(int hour, int minute, bool expected) =>
        Assert.Equal(expected, WorkSchedule.IsWorkingTime(Scheduled, Monday(hour, minute), Zone));

    [Fact]
    public void DaysUseLocalDateAndCanIncludeWeekendsAndMidnightEnd()
    {
        Assert.Null(WorkSchedule.GetPeriod(Scheduled, new(2026, 10, 18), Zone));
        var settings = Scheduled with { WorkingDays = WorkDays.Sunday, WorkStartMinutes = 0, WorkEndMinutes = 1440 };
        Assert.True(WorkSchedule.IsWorkingTime(settings, new(2026, 10, 17, 22, 30, 0, TimeSpan.Zero), Zone));
        Assert.False(WorkSchedule.IsWorkingTime(settings, Monday(0), Zone));
    }

    [Fact]
    public void DstDaysUseActualOffsetsAndMissingTimesAdvanceToValidTime()
    {
        var settings = Scheduled with { WorkingDays = WorkDays.Sunday, WorkStartMinutes = 60, WorkEndMinutes = 240 };
        var spring = WorkSchedule.GetPeriod(settings, new(2026, 3, 29), Zone)!;
        var autumn = WorkSchedule.GetPeriod(settings, new(2026, 10, 25), Zone)!;
        Assert.Equal(TimeSpan.FromHours(2), spring.End - spring.Start);
        Assert.Equal(TimeSpan.FromHours(4), autumn.End - autumn.Start);
        var missing = WorkSchedule.GetPeriod(settings with { WorkStartMinutes = 150 }, new(2026, 3, 29), Zone)!;
        Assert.Equal(3, missing.Start.Hour);
        Assert.Equal(TimeSpan.FromHours(1), missing.End - missing.Start);
        Assert.Null(WorkSchedule.GetPeriod(settings with { WorkStartMinutes = 130, WorkEndMinutes = 160 }, new(2026, 3, 29), Zone));
    }

    [Fact]
    public void AutomationActsAtBoundariesAndLeavesManualTogglesAloneBetweenThem()
    {
        var automation = new RecordingAutomation();
        Assert.False(automation.Evaluate(Scheduled, Monday(6), Zone));
        Assert.Null(automation.Evaluate(Scheduled, Monday(6, 59), Zone));
        Assert.True(automation.Evaluate(Scheduled, Monday(7), Zone));
        Assert.Null(automation.Evaluate(Scheduled, Monday(8), Zone));
        Assert.Null(automation.Evaluate(Scheduled with { MinimumActivitySeconds = 0 }, Monday(9), Zone));
        Assert.False(automation.Evaluate(Scheduled, Monday(15), Zone));
        Assert.Null(automation.Evaluate(Scheduled, Monday(16), Zone));
        Assert.True(automation.Evaluate(Scheduled, Monday(7).AddDays(1), Zone));
    }

    [Fact]
    public void AutomationRechecksAfterSleepOrScheduleChanges()
    {
        var automation = new RecordingAutomation();
        Assert.True(automation.Evaluate(Scheduled, Monday(8), Zone));
        Assert.True(automation.Evaluate(Scheduled, Monday(8).AddDays(1), Zone));
        Assert.False(automation.Evaluate(Scheduled, Monday(8).AddDays(5), Zone));
        Assert.True(automation.Evaluate(Scheduled with { WorkingDays = WorkDays.All }, Monday(8).AddDays(5), Zone));
        Assert.False(automation.Evaluate(Scheduled with { WorkStartMinutes = 600 }, Monday(8), Zone));
        Assert.True(automation.Evaluate(new(), Monday(8), Zone));
        Assert.Null(automation.Evaluate(new(RecordOnLaunch: false), Monday(9), Zone));
    }

    [Fact]
    public void ManualModeStartsPausedAndScheduleCanBeEnabledImmediately()
    {
        var automation = new RecordingAutomation();
        Assert.False(automation.Evaluate(new(RecordOnLaunch: false), Monday(8), Zone));
        Assert.True(automation.Evaluate(Scheduled, Monday(8), Zone));
        Assert.False(automation.Evaluate(new(RecordOnLaunch: false), Monday(8), Zone));
        Assert.True(new RecordingAutomation().Evaluate(new(), Monday(22), Zone));
        Assert.False(new RecordingAutomation().Evaluate(Scheduled, Monday(22), Zone));
    }

    private static ActivityInterval Activity(DateTimeOffset start, DateTimeOffset end) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new(1, 2, "app", "title"), start, end, false);

    [Fact]
    public void TimelineIncludesWorkHoursRegardlessOfRecordingModeAndActivityCount()
    {
        var date = new DateOnly(2026, 10, 19);
        var manual = new ApplicationSettings(RecordOnLaunch: false);
        var empty = TimelineRange.Calculate(date, manual, [], Zone);
        Assert.Equal(Monday(7), empty.Start);
        Assert.Equal(Monday(15), empty.End);
        var shortActivity = TimelineRange.Calculate(date, manual, [Activity(Monday(9), Monday(10))], Zone);
        Assert.Equal(empty, shortActivity);
        var wider = TimelineRange.Calculate(date, Scheduled, [Activity(Monday(5, 30), Monday(17, 10))], Zone);
        Assert.Equal(Monday(5), wider.Start);
        Assert.Equal(Monday(18), wider.End);
        var filtered = TimelineRange.Calculate(date, Scheduled with { WorkingDays = WorkDays.None }, [Activity(Monday(9), Monday(10))], Zone);
        Assert.Equal(Monday(9), filtered.Start);
        Assert.Equal(Monday(11), filtered.End);
        var weekend = TimelineRange.Calculate(new(2026, 10, 18), manual, [], Zone);
        Assert.Equal(new DateTime(2026, 10, 18), weekend.Start.Date);
        Assert.Equal(6, weekend.Start.Hour);
        Assert.Equal(18, weekend.End.Hour);
    }
}
