using System;
using System.Runtime.InteropServices;

namespace SolidRecords.Windows;

public static class WindowAppearance
{
    public static void ApplyRoundedCorners(nint window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

        const uint windowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
        var round = 2; // DWMWCP_ROUND; DWM keeps maximized and snapped windows square.
        // A visual preference is best-effort; unsupported desktop configurations may ignore it.
        _ = DwmSetWindowAttribute(window, windowCornerPreference, ref round, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);
}
