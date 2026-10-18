using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using SolidRecords.Core.Updates;

namespace SolidRecords.Windows.Shell;

public sealed class UpdateInstaller(HttpClient http, string directory)
{
    public async Task<string> DownloadAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        if (!release.IsValid) throw new InvalidOperationException("Neplatný inštalátor aktualizácie.");
        var destination = Path.Combine(directory, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var path = Path.Combine(destination, "SolidRecords.msi");
        try
        {
            using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new IOException("Veľkosť inštalátora nezodpovedá vydaniu.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > release.Size) throw new IOException("Inštalátor presiahol očakávanú veľkosť.");
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                if (total != release.Size) throw new IOException("Inštalátor sa nestiahol celý.");
            }
            await using var downloaded = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Kontrolný súčet inštalátora nesúhlasí. Inštalácia bola zrušená.");
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
        // The elevated helper waits for normal shutdown, including DI/store disposal. No MSI file is
        // opened while the old executable is running; cancellation of UAC leaves recording untouched.
        var script = CreateHelperScript(path, sha256);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "
                + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))
        };
        return Process.Start(start) ?? throw new IOException("Inštalátor sa nepodarilo spustiť.");
    }

    public static string CreateHelperScript(string path, string sha256)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $parent = Get-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                if ($parent) { $parent | Wait-Process -Timeout 120 }
                $msi = {{Quote(path)}}
                if ((Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash -ne {{Quote(sha256)}}) {
                    throw 'Kontrolný súčet inštalátora nesúhlasí.'
                }
                $log = Join-Path (Split-Path -LiteralPath $msi) 'install.log'
                $arguments = '/i "' + $msi + '" /quiet /norestart /L*v "' + $log + '"'
                $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
                if ($process.ExitCode -notin @(0, 1641, 3010)) { throw "Inštalácia zlyhala ($($process.ExitCode)). Protokol: $log" }
                Remove-Item -LiteralPath $msi
            } catch {
                Add-Type -AssemblyName System.Windows.Forms
                [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Aktualizácia SolidRecords') | Out-Null
            }
            """;
    }
}
