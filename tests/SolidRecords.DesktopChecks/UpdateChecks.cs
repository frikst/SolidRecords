using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Xml.Linq;
using SolidRecords.App.Updates;
using SolidRecords.App.Infrastructure;
using SolidRecords.Core;
using SolidRecords.Core.Updates;
using SolidRecords.Windows.Shell;

namespace SolidRecords.DesktopChecks;

internal static class UpdateChecks
{
    public static async Task RunAsync()
    {
        var directory = Path.GetFullPath(Path.Combine("artifacts", "update-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var release = new UpdateRelease("v2.0.0", "https://github.com/frikst/SolidRecords/releases/download/v2.0.0/SolidRecords-2.0.0-win-x64.msi",
            bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        using var http = new HttpClient(new Handler(bytes));
        var installer = new UpdateInstaller(http, directory);
        var path = await installer.DownloadAsync(release, default);
        Require((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes), "MSI bytes changed during download.");
        foreach (var invalid in new[] { release with { Sha256 = new string('a', 64) }, release with { Size = 1 }, release with { Size = 6 } })
        {
            try { await installer.DownloadAsync(invalid, default); throw new InvalidOperationException("Invalid MSI accepted."); }
            catch (IOException) { }
        }
        Require(Directory.GetFiles(directory, "*.msi", SearchOption.AllDirectories).Length == 1, "Partial MSI was not deleted.");
        var store = new UpdateStateStore(directory);
        var state = new UpdateState(DateTimeOffset.UtcNow.AddDays(1), "\"etag\"", release, DateTimeOffset.UtcNow.AddHours(4), ["v1.1.0"]);
        await store.SaveAsync(state, default);
        var loaded = await new UpdateStateStore(directory).ReadAsync(default);
        Require(loaded.ETag == state.ETag && loaded.Latest == release && loaded.NextCheckAt == state.NextCheckAt
            && loaded.NextNotificationAt == state.NextNotificationAt && loaded.IgnoredVersions!.SequenceEqual(state.IgnoredVersions!), "Update cache did not persist.");
        // Closing the app while a download is pending must cancel and drain it before shutdown.
        using var delayedHandler = new DelayedHandler();
        using var delayedHttp = new HttpClient(delayedHandler);
        var preferences = new PreferencesStore(new SettingsStore(), directory);
        await preferences.LoadAsync();
        var checker = new UpdateChecker(new UnusedClient(), store, new SilentNotifications(), new(1, 0, 0));
        await using (var controller = new UpdateController(checker, new UpdateInstaller(delayedHttp, directory), preferences))
        {
            Exception? failure = null;
            controller.Start(ex => failure = ex);
            var action = controller.HandleActionAsync("install", release.Tag, () => throw new InvalidOperationException("Cancelled download launched the installer."));
            await delayedHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await controller.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await action.WaitAsync(TimeSpan.FromSeconds(5));
            Require(failure is null, "Shutdown cancellation surfaced as an update failure.");
        }
        Require(Directory.GetFiles(directory, "*.msi", SearchOption.AllDirectories).Length == 1, "Cancelled download was not cleaned up.");
        // Explicit installation works with automatic checks disabled and is drained on shutdown too.
        using var manualHandler = new DelayedHandler();
        using var manualHttp = new HttpClient(manualHandler);
        var manualPreferences = new PreferencesStore(new SettingsStore(false), directory);
        await manualPreferences.LoadAsync();
        var manualChecker = new UpdateChecker(new CachedClient(), store, new SilentNotifications(), new(1, 0, 0));
        await using (var controller = new UpdateController(manualChecker, new UpdateInstaller(manualHttp, directory), manualPreferences))
        {
            var action = controller.UpdateNowAsync(() => throw new InvalidOperationException("Cancelled manual update launched the installer."));
            await manualHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(await controller.UpdateNowAsync(() => Task.CompletedTask) == "Aktualizácia už prebieha.", "Concurrent manual updates were accepted.");
            await controller.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Require(await action.WaitAsync(TimeSpan.FromSeconds(5)) == "Aktualizácia bola zrušená.", "Manual shutdown cancellation was not reported.");
            Require(!manualPreferences.Current.CheckForUpdates, "Manual update enabled automatic checks.");
        }
        Require(Directory.GetFiles(directory, "*.msi", SearchOption.AllDirectories).Length == 1, "Cancelled manual download was not cleaned up.");
        var xml = XDocument.Parse(UpdateNotifications.CreateContent(release).GetToastContent().GetContent());
        var actions = xml.Descendants("action").ToArray();
        Require(actions.Length == 3, "Notification does not contain all three actions.");
        foreach (var action in new[] { "install", "later", "ignore" })
            Require(actions.Any(element => ((string?)element.Attribute("arguments"))?.Contains("action=" + action, StringComparison.Ordinal) == true
                && ((string?)element.Attribute("arguments"))?.Contains("tag=v2.0.0", StringComparison.Ordinal) == true), "Notification action lost its version or command.");
        await File.WriteAllTextAsync(Path.Combine(directory, "notification.xml"), xml.ToString());
        await File.WriteAllTextAsync(Path.Combine(directory, "installer-helper.ps1"), UpdateInstaller.CreateHelperScript(path.Replace("SolidRecords.msi", "apostrophe's test.msi"), release.Sha256));
        Console.WriteLine($"PASS: Update cache, notification actions, MSI digest/size validation and partial-file cleanup. Helper source: {directory}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SettingsStore(bool checkForUpdates = true) : ISettingsStore
    {
        public Task<ApplicationSettings?> ReadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult<ApplicationSettings?>(new(CheckForUpdates: checkForUpdates));
        public Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UnusedClient : IReleaseClient
    {
        public Task<ReleaseCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken) => throw new InvalidOperationException("Cached version triggered a network request.");
    }

    private sealed class CachedClient : IReleaseClient
    {
        public Task<ReleaseCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken) => Task.FromResult(new ReleaseCheckResult(true));
    }

    private sealed class SilentNotifications : IUpdateNotifier
    {
        public void Show(UpdateRelease release) { }
        public void Clear() { }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancelled request completed.");
        }
    }

    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
