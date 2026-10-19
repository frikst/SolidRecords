namespace SolidRecords.Windows.Capture;

/// <summary>Explicit WTS restrictions take precedence over desktop availability probes.</summary>
internal sealed class SessionCaptureState
{
    private bool _locked;
    private bool _disconnected;
    private bool _loggedOff;
    public bool IsEnding { get; set; }

    public void ApplyWtsChange(int change)
    {
        switch (change)
        {
            case 1: case 3: _disconnected = false; break;
            case 2: case 4: _disconnected = true; break;
            case 5: _loggedOff = false; break;
            case 6: _loggedOff = true; break;
            case 7: _locked = true; break;
            case 8: _locked = false; break;
        }
    }

    public bool CanCapture(bool desktopActive) =>
        desktopActive && !_locked && !_disconnected && !_loggedOff && !IsEnding;
}
