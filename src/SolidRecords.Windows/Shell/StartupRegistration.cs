using Microsoft.Win32;
using System.IO;
using SolidRecords.Core.Localization;

namespace SolidRecords.Windows.Shell;

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "SolidRecords";

    public static bool IsEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(Name) is string; }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException(Strings.Get("ExecutableUnavailable"));
            if (!Path.GetFileNameWithoutExtension(executable).Equals("SolidRecords.App", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Strings.Get("StartupFromBuild"));
            key.SetValue(Name, $"\"{executable}\" --background");
        }
        else key.DeleteValue(Name, false);
    }
}
