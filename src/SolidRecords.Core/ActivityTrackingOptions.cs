namespace SolidRecords.Core;

/// <summary>Shared defaults, ready to be supplied from application settings later.</summary>
public sealed class ActivityTrackingOptions
{
    private static readonly TimeSpan DefaultThreshold = TimeSpan.FromSeconds(15);
    public static ActivityTrackingOptions Default { get; } = new();

    public ActivityTrackingOptions(TimeSpan? minimumActivityDuration = null, TimeSpan? maximumMergeGap = null)
    {
        MinimumActivityDuration = minimumActivityDuration ?? DefaultThreshold;
        MaximumMergeGap = maximumMergeGap ?? DefaultThreshold;
        ArgumentOutOfRangeException.ThrowIfLessThan(MinimumActivityDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumMergeGap, TimeSpan.Zero);
    }

    public TimeSpan MinimumActivityDuration { get; }
    public TimeSpan MaximumMergeGap { get; }
}
