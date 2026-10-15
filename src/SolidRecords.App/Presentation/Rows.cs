using SolidRecords.Core;

namespace SolidRecords.App.Presentation;

public sealed record ActivityRow(ActivityInterval Interval)
{
    public string Application => Interval.Window.Application;
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

public sealed record EventRow(ActivityEvent Event)
{
    public string Time => Event.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    public string Application => Event.Application ?? "—";
    public string ProcessId => Event.ProcessId?.ToString() ?? "—";
    public string Description => Event.Window?.Title ?? Event.Reason ?? "—";
    public string Url => Event.Window?.Url ?? "—";
    public string Kind => Event.Kind switch
    {
        EventKind.RecordingStarted => "Spustenie záznamu",
        EventKind.RecordingStopped => "Zastavenie záznamu",
        EventKind.SessionActive => "Aktívna relácia",
        EventKind.SessionInactive => "Neaktívna relácia",
        EventKind.ProcessStarted => "Spustenie programu",
        EventKind.ProcessStopped => "Ukončenie programu",
        EventKind.ProcessObserved => "Už spustený program",
        EventKind.WindowActivated => "Aktivovanie okna",
        EventKind.WindowDeactivated => "Deaktivovanie okna",
        EventKind.DocumentChanged => "Zmena dokumentu / URL",
        _ => Event.Kind.ToString()
    };
}
