using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using SolidRecords.Core.Updates;
using SolidRecords.Core.Localization;

namespace SolidRecords.Windows.Shell;

public sealed class UpdateInstaller(HttpClient http, string directory)
{
    public async Task<string> DownloadAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        if (!release.IsValid) throw new InvalidOperationException(Strings.Get("InvalidInstaller"));
        var destination = Path.Combine(directory, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var path = Path.Combine(destination, "SolidRecords.msi");
        try
        {
            using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new IOException(Strings.Get("InstallerSizeMismatch"));
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > release.Size) throw new IOException(Strings.Get("InstallerTooLarge"));
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                if (total != release.Size) throw new IOException(Strings.Get("IncompleteDownload"));
            }
            await using var downloaded = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException(Strings.Get("ChecksumMismatch"));
            return path;
        }
        catch
        {
            File.Delete(path);
            Directory.Delete(destination);
            throw;
        }
    }

    public static Process LaunchAfterExit(string path, string sha256)
    {
        // The restart helper keeps the original user's token, even with another account at UAC.
        // Only the installer is elevated; cancellation leaves recording untouched.
        var pipeName = "SolidRecords.Update." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, pipeName + ".Ready");
        var helper = StartPowerShell(CreateHelperScript(path, sha256, pipeName), elevated: true);
        try
        {
            using var restart = StartPowerShell(CreateRestartScript(pipeName), elevated: false);
            if (!ready.WaitOne(TimeSpan.FromSeconds(15)))
            {
                if (!restart.HasExited) restart.Kill();
                throw new IOException(Strings.Get("RestartNotReady"));
            }
            return helper;
        }
        catch
        {
            // Without a restart client the helper times out before installing anything.
            helper.Dispose();
            throw;
        }
    }

    private static Process StartPowerShell(string script, bool elevated)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = elevated, Verb = elevated ? "runas" : "",
            CreateNoWindow = !elevated, WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "
                + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))
        };
        return Process.Start(start) ?? throw new IOException(Strings.Get("InstallerStartFailed"));
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    public static string CreateHelperScript(string path, string sha256, string pipeName)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $env:PSModulePath = (Join-Path $PSHOME 'Modules') + ';' + $env:PSModulePath
            $pipe = $null
            $writer = $null
            try {
                # Default pipe security allows the original user to read even with another UAC account.
                $pipe = [IO.Pipes.NamedPipeServerStream]::new({{Quote(pipeName)}}, [IO.Pipes.PipeDirection]::Out, 1,
                    [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous)
                $connection = $pipe.BeginWaitForConnection($null, $null)
                if (-not $connection.AsyncWaitHandle.WaitOne(60000)) { throw {{Quote(Strings.Get("RestartDisconnected"))}} }
                $pipe.EndWaitForConnection($connection)
                $connection.AsyncWaitHandle.Dispose()
                $writer = [IO.StreamWriter]::new($pipe)
                $writer.AutoFlush = $true
                $parent = Get-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                if ($parent) { $parent | Wait-Process -Timeout 120 }
                $msi = {{Quote(path)}}
                if ((Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash -ne {{Quote(sha256)}}) {
                    throw {{Quote(Strings.Get("HelperChecksum"))}}
                }
                $log = Join-Path (Split-Path -LiteralPath $msi) 'install.log'
                $arguments = '/i "' + $msi + '" /quiet /norestart /L*v "' + $log + '"'
                $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
                if ($process.ExitCode -notin @(0, 1641, 3010)) { throw ({{Quote(Strings.Get("InstallFailure"))}} -f $process.ExitCode, $log) }
                # Cleanup must not prevent restarting a successfully installed update.
                Remove-Item -LiteralPath $msi -ErrorAction SilentlyContinue
                $writer.WriteLine('installed')
            } catch {
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, {{Quote(Strings.Get("UpdateTitle"))}}) | Out-Null
            } finally {
                if ($writer) { $writer.Dispose() }
                if ($pipe) { $pipe.Dispose() }
            }
            """;
    }

    public static string CreateRestartScript(string pipeName)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $env:PSModulePath = (Join-Path $PSHOME 'Modules') + ';' + $env:PSModulePath
            $pipe = $null
            $reader = $null
            try {
                $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', {{Quote(pipeName)}}, [IO.Pipes.PipeDirection]::In,
                    [IO.Pipes.PipeOptions]::Asynchronous, [Security.Principal.TokenImpersonationLevel]::Identification)
                $pipe.Connect(60000)
                $ready = [Threading.EventWaitHandle]::OpenExisting({{Quote(pipeName + ".Ready")}})
                try { $ready.Set() | Out-Null } finally { $ready.Dispose() }
                $reader = [IO.StreamReader]::new($pipe)
                # Keep waiting for long installations too; a terminated installer helper closes the pipe.
                $result = $reader.ReadLine()
                if ($result -ne 'installed') { exit }
                # Read the machine-wide path; never restart an outdated portable executable.
                $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,
                    [Microsoft.Win32.RegistryView]::Registry64)
                try {
                    $key = $registry.OpenSubKey('Software\SolidRecords\Installer')
                    try { $folder = if ($key) { $key.GetValue('InstallDirectory') } } finally { if ($key) { $key.Dispose() } }
                } finally { $registry.Dispose() }
                if (-not $folder) { throw {{Quote(Strings.Get("MissingInstallPath"))}} }
                $application = Join-Path $folder 'SolidRecords.App.exe'
                Start-Process -FilePath $application -WorkingDirectory $folder -WindowStyle Hidden -ErrorAction Stop | Out-Null
            } catch {
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, {{Quote(Strings.Get("RestartTitle"))}}) | Out-Null
            } finally {
                if ($reader) { $reader.Dispose() }
                if ($pipe) { $pipe.Dispose() }
            }
            """;
    }
}
