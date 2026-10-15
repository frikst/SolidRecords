using SolidRecords.Core;

namespace SolidRecords.App.Presentation;

public sealed record ActivityRow(ActivityInterval Interval)
{
    public string Application => Interval.Window.Application;
    public int ProcessId => Interval.Window.ProcessId;
    public string Description => string.IsNullOrWhiteSpace(Interval.Window.Title) ? "Bez názvu" : Interval.Window.Title;
    public string Url => Interval.Window.Url ?? "—";
    public string Start => Interval.Start.ToLocalTime().ToString("HH:mm:ss");
    public string End => Interval.End.ToLocalTime().ToString("HH:mm:ss");
    public string Duration => FormatDuration(Interval.Duration);
    public string State => Interval.IsOpen ? "Práve aktívne" : "";
    public static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours} h {duration.Minutes:00} min"
        : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s" : $"{duration.Seconds} s";
}

