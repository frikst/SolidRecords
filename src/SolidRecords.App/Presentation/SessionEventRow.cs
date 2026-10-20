using SolidRecords.Core;

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
        "WTS:5" or "Logon" => "Prihlásenie používateľa",
        "WTS:8" or "Unlock" => "Odomknutie počítača",
        "WTS:6" or "Logoff" => "Odhlásenie používateľa",
        "WTS:7" or "Lock" => "Zamknutie počítača",
        "Shutdown" => "Vypnutie počítača",
        "PowerOn" => "Zapnutie počítača",
        "Suspend" => "Uspatie počítača",
        "Resume" => "Prebudenie počítača",
        "WTS:1" or "WTS:3" => "Pripojenie relácie",
        "WTS:2" or "WTS:4" => "Odpojenie relácie",
        "SessionEnding" => "Odhlásenie alebo vypnutie počítača",
        "ApplicationStartup" => "Stav pri spustení aplikácie",
        "RecordingBaseline" => "Stav pri spustení záznamu",
        _ => "Zmena stavu relácie"
    };
    public string Summary => $"{(Event.Reason == "ApplicationStartup" ? "Aktívna relácia" :
        IsActive ? "Aktivovaná relácia" : "Deaktivovaná relácia")} — {Description}";
    public string Label => $"{Start}  ·  {Summary}";
}
