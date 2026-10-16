namespace SolidRecords.Core;

/// <summary>Shared recording and report thresholds.</summary>
public sealed class ActivityTrackingOptions
{
    private static readonly TimeSpan DefaultThreshold = TimeSpan.FromSeconds(15);
    public static ActivityTrackingOptions Default { get; } = new();

    public ActivityTrackingOptions(TimeSpan? minimumActivityDuration = null, TimeSpan? maximumMergeGap = null)
    {
        _minimumActivityTicks = (minimumActivityDuration ?? DefaultThreshold).Ticks;
        MaximumMergeGap = maximumMergeGap ?? DefaultThreshold;
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumActivityDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumMergeGap, TimeSpan.Zero);
    }

    private long _minimumActivityTicks;
    public TimeSpan MinimumActivityDuration => TimeSpan.FromTicks(Interlocked.Read(ref _minimumActivityTicks));

    public void SetMinimumActivityDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Interlocked.Exchange(ref _minimumActivityTicks, duration.Ticks);
    }
    public TimeSpan MaximumMergeGap { get; }
}
