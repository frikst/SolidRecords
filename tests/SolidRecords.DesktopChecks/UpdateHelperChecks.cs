using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SolidRecords.Windows.Shell;

namespace SolidRecords.DesktopChecks;

internal static class UpdateHelperChecks
{
    public static async Task RunAsync(string directory)
    {
        foreach (var code in new[] { 0, 1641, 3010, 1603 })
            await CheckAsync(directory, code, validHash: true, shutdownCompletes: true);
        await CheckAsync(directory, 0, validHash: false, shutdownCompletes: true);
        await CheckAsync(directory, 0, validHash: true, shutdownCompletes: false);
        Console.WriteLine("PASS: Real PowerShell helper IPC restarts only after successful installation (0/1641/3010); MSI failure, digest failure and shutdown timeout do not restart.");
    }

    private static async Task CheckAsync(string directory, int code, bool validHash, bool shutdownCompletes)
    {
        var scenario = Path.Combine(directory, Guid.NewGuid().ToString("N") + " apostrophe's test");
        Directory.CreateDirectory(scenario);
        var msi = Path.Combine(scenario, "test.msi");
        await File.WriteAllBytesAsync(msi, [1, 2, 3]);
        var hash = Convert.ToHexString(SHA256.HashData([1, 2, 3]));
        var marker = Path.Combine(scenario, "restarted.txt");
        var installed = Path.Combine(scenario, "installed.txt");
        var pipe = "SolidRecords.Update.Test." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, pipe + ".Ready");
        var helper = UpdateInstaller.CreateHelperScript(msi, validHash ? hash : new string('0', 64), pipe);
        // Execute the generated scripts and real IPC, replacing only external side effects.
        // Never invoke MSI, read/write the user's registry, or launch the real tracker.
        helper = Regex.Replace(helper, @"\$parent = Get-Process[^\r\n]+", "$parent = $null");
        if (!shutdownCompletes)
            helper = helper.Replace("if ($parent) { $parent | Wait-Process -Timeout 120 }", "throw 'Simulated shutdown timeout'");
        helper = Regex.Replace(helper, @"\$process = Start-Process[^\r\n]+",
            _ => $"[IO.File]::WriteAllText({Quote(installed)}, 'installed'); $process = [pscustomobject]@{{ ExitCode = {code} }}");
        helper = SuppressDialog(helper);
        var restart = UpdateInstaller.CreateRestartScript(pipe);
        var registryStart = restart.IndexOf("    # Read the machine-wide path", StringComparison.Ordinal);
        if (registryStart < 0) throw new InvalidOperationException("Restart test could not isolate registry access.");
        var launchStart = restart.IndexOf("    Start-Process", registryStart, StringComparison.Ordinal);
        if (launchStart < 0) throw new InvalidOperationException("Restart test could not isolate application launch.");
        restart = restart[..registryStart] + $"    $folder = {Quote(scenario)}\n" + restart[launchStart..];
        restart = Regex.Replace(restart, @"Start-Process[^\r\n]+",
            _ => $"if (-not [IO.File]::Exists({Quote(installed)})) {{ throw 'Restart preceded installation' }}; [IO.File]::WriteAllText({Quote(marker)}, 'restarted')");
        restart = SuppressDialog(restart);
        using var installerProcess = Start(helper);
        using var restartProcess = Start(restart);
        var installerOutput = installerProcess.StandardError.ReadToEndAsync();
        var restartOutput = restartProcess.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await Task.WhenAll(installerProcess.WaitForExitAsync(timeout.Token), restartProcess.WaitForExitAsync(timeout.Token));
        }
        finally
        {
            if (!installerProcess.HasExited) installerProcess.Kill(entireProcessTree: true);
            if (!restartProcess.HasExited) restartProcess.Kill(entireProcessTree: true);
        }
        var success = validHash && shutdownCompletes && code is 0 or 1641 or 3010;
        if (!ready.WaitOne(0) || File.Exists(marker) != success || restartProcess.ExitCode != 0
            || (installerProcess.ExitCode == 0) != success
            || File.Exists(installed) != (validHash && shutdownCompletes))
            throw new InvalidOperationException($"Update helper scenario {code}/{validHash}/{shutdownCompletes} failed: {await installerOutput} {await restartOutput}");
        if (success && File.Exists(msi)) throw new InvalidOperationException("Successful installation left MSI behind.");
        if (!success && !File.Exists(msi)) throw new InvalidOperationException("Failed installation removed MSI diagnostics.");
    }

    private static string SuppressDialog(string script) => Regex.Replace(script,
        @"\[System.Windows.Forms.MessageBox\]::Show[^\r\n]+", _ => "[Console]::Error.WriteLine($_.Exception.Message); exit 1");

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static Process Start(string script) => Process.Start(new ProcessStartInfo(
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
    }) ?? throw new IOException("Test PowerShell did not start.");
}
