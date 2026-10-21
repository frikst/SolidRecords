using SolidRecords.Core.Localization;
using SolidRecords.Core;
using SolidRecords.Core.Recording;
using SolidRecords.Windows.Capture;

namespace SolidRecords.DesktopChecks;

internal static class SessionCaptureChecks
{
    public static void Run()
    {
        void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        var state = new SessionCaptureState();
        Require(state.CanCapture(true) && !state.CanCapture(false), "Desktop availability is ignored");
        state.ApplyWtsChange(7);
        for (var tick = 0; tick < 60; tick++)
            Require(!state.CanCapture(true), "A desktop probe reactivated a locked workstation");
        state.ApplyWtsChange(3);
        Require(!state.CanCapture(true), "Connecting unlocked the workstation");
        state.ApplyWtsChange(4);
        state.ApplyWtsChange(8);
        Require(!state.CanCapture(true), "Unlocking reconnected a disconnected session");
        state.ApplyWtsChange(3);
        Require(state.CanCapture(true), "An unlocked connected session stayed blocked");
        state.ApplyWtsChange(6);
        state.ApplyWtsChange(8);
        Require(!state.CanCapture(true), "Unlocking reversed logoff");
        state.ApplyWtsChange(5);
        Require(state.CanCapture(true), "Logon did not clear logoff");
        state.IsEnding = true;
        Require(!state.CanCapture(true), "A probe reversed shutdown");
        state.IsEnding = false;
        Require(state.CanCapture(true), "Cancelled shutdown stayed blocked");

        // Reproduce the screenshot: lock, a positive probe one second later,
        // an unavailable desktop before unlock, then the explicit unlock.
        state = new();
        var engine = new RecordingEngine();
        var at = new DateTimeOffset(2026, 10, 19, 16, 49, 23, TimeSpan.Zero);
        engine.Apply(new(SignalKind.RecordingStarted, at));
        engine.Apply(new(SignalKind.SessionActive, at));
        var events = new List<ActivityEvent>();
        void Probe(DateTimeOffset time, bool desktopActive, string reason)
        {
            events.AddRange(engine.Apply(new(state.CanCapture(desktopActive)
                ? SignalKind.SessionActive : SignalKind.SessionInactive, time, Reason: reason)).Events);
        }
        state.ApplyWtsChange(7);
        Probe(at, true, "WTS:7");
        Probe(at.AddSeconds(1), true, "DesktopState");
        Probe(at.AddHours(1), true, "Resume");
        Probe(at.AddHours(1).AddMinutes(3), false, "SecureDesktop");
        state.ApplyWtsChange(8);
        Probe(at.AddHours(1).AddMinutes(3).AddSeconds(2), true, "WTS:8");
        Require(events.Count == 2 && events[0].Reason == "WTS:7" && events[1].Reason == "WTS:8",
            "Lock/unlock generated intermediate session transitions");
        // Exercise the real message handler too: power resume must retain the
        // lock latch, rather than replacing it with a fresh unrestricted state.
        var source = new WindowsActivitySource();
        try
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var published = new List<CaptureSignal>();
            typeof(WindowsActivitySource).GetField("_publish", flags)!.SetValue(source, (Action<CaptureSignal>)published.Add);
            source.PublishInitialSession(true);
            source.PublishInitialSession(true);
            Require(published.Count == 2 && published[0].Reason == "ApplicationStartup"
                && published[1].Reason == "RecordingBaseline", "Recording restart repeated the application startup observation");
            var startup = new SolidRecords.App.Presentation.SessionEventRow(new(Guid.NewGuid(), Guid.NewGuid(),
                EventKind.SessionActive, at, Reason: published[0].Reason));
            Require(startup.Summary == Strings.Get("ActiveSession") + " — " + Strings.Get("ApplicationStartup"),
                "Startup observation is presented as a precise logon");
            var onMessage = typeof(WindowsActivitySource).GetMethod("OnMessage", flags)!;
            onMessage.Invoke(source, new object[] { (nint)0, 0x02B1, (nint)7, (nint)0, false });
            onMessage.Invoke(source, new object[] { (nint)0, 0x0218, (nint)18, (nint)0, false });
            var actualState = (SessionCaptureState)typeof(WindowsActivitySource).GetField("_sessionState", flags)!.GetValue(source)!;
            Require(!actualState.CanCapture(true), "The real resume handler discarded the WTS lock state");
        }
        finally { source.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        Console.WriteLine("Session capture: explicit lock, disconnect, logoff and shutdown override desktop probes; screenshot sequence produces only lock/unlock.");
    }
}
