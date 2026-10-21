using SolidRecords.Core;
using SolidRecords.Core.Localization;

namespace SolidRecords.App.Presentation;

public sealed class SessionEventRow(ActivityEvent entry) : ObservableObject, IActivityListRow
{
    public ActivityEvent Event { get; private set; } = entry;
    public void Update(ActivityEvent entry)
    {
        if (Event == entry) return;
        Event = entry;
        foreach (var name in new[] { nameof(Event), nameof(Timestamp), nameof(IsActive), nameof(Start), nameof(Description), nameof(Summary), nameof(Label) })
            Changed(name);
    }
    public bool IsSeparator => true;
    public Guid Id => Event.Id;
    public DateTimeOffset Timestamp => Event.Timestamp;
    public bool IsActive => Event.Kind == EventKind.SessionActive;
    public string Start => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string Application => "";
    public int ProcessId => 0;
    public string Url => "";
    public string End => "";
    public string Duration => "";
    public TimeSpan DurationValue => TimeSpan.Zero;
    public string Description => Event.Reason switch
    {
        "WTS:5" or "Logon" => Strings.Get("Logon"),
        "WTS:8" or "Unlock" => Strings.Get("Unlock"),
        "WTS:6" or "Logoff" => Strings.Get("Logoff"),
        "WTS:7" or "Lock" => Strings.Get("Lock"),
        "Shutdown" => Strings.Get("Shutdown"),
        "PowerOn" => Strings.Get("PowerOn"),
        "Suspend" => Strings.Get("Suspend"),
        "Resume" => Strings.Get("Resume"),
        "WTS:1" or "WTS:3" => Strings.Get("SessionConnected"),
        "WTS:2" or "WTS:4" => Strings.Get("SessionDisconnected"),
        "SessionEnding" => Strings.Get("SessionEnding"),
        "ApplicationStartup" => Strings.Get("ApplicationStartup"),
        "RecordingBaseline" => Strings.Get("RecordingBaseline"),
        _ => Strings.Get("SessionChanged")
    };
    public string Summary => $"{(Event.Reason == "ApplicationStartup" ? Strings.Get("ActiveSession") :
        IsActive ? Strings.Get("ActivatedSession") : Strings.Get("DeactivatedSession"))} — {Description}";
    public string Label => $"{Start}  ·  {Summary}";
}
