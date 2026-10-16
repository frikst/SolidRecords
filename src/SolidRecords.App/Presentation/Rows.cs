using SolidRecords.Core;

namespace SolidRecords.App.Presentation;

public sealed class ActivityRow(ActivityInterval interval) : ObservableObject
{
    public ActivityInterval Interval { get; private set; } = interval;
    public void Update(ActivityInterval value)
    {
        if (Interval.Id == value.Id && Interval.Window == value.Window && Interval.Start == value.Start &&
            Interval.End == value.End && Interval.IsOpen == value.IsOpen && Interval.Spans.SequenceEqual(value.Spans)) return;
        Interval = value;
        foreach (var name in new[] { nameof(Interval), nameof(Application), nameof(ProcessId), nameof(Description),
                     nameof(Url), nameof(Start), nameof(End), nameof(Duration), nameof(DurationValue), nameof(State) }) Changed(name);
    }

    public string Application => Interval.Window.Application;
    public int ProcessId => Interval.Window.ProcessId;
    public string Description => string.IsNullOrWhiteSpace(Interval.Window.Title) ? "Bez názvu" : Interval.Window.Title;
    public string Url => Interval.Window.Url ?? "—";
    public string Start => Interval.Start.ToLocalTime().ToString("HH:mm:ss");
    public string End => Interval.IsOpen ? "-" : Interval.End.ToLocalTime().ToString("HH:mm:ss");
    public TimeSpan DurationValue => Interval.Duration;
    public string Duration => Interval.IsOpen ? "-" : FormatDuration(Interval.Duration);
    public string State => Interval.IsOpen ? "Práve aktívne" : "";
    public static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours} h {duration.Minutes:00} min"
        : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s" : $"{duration.Seconds} s";
}

