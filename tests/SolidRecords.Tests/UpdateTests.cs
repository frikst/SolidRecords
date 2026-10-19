using Xunit;
using System.Net;
using System.Text;
using System.Text.Json;
using SolidRecords.Core;
using SolidRecords.Core.Updates;

namespace SolidRecords.Tests;

public sealed class UpdateTests
{
    private static readonly UpdateRelease Release = new("v2.0.0",
        "https://github.com/frikst/SolidRecords/releases/download/v2.0.0/SolidRecords-2.0.0-win-x64.msi", 123, new string('a', 64));

    [Fact]
    public async Task DailyDeadlineAndEtagSurviveRestartAnd304KeepsCachedRelease()
    {
        var clock = new Clock();
        var store = new StateStore();
        var notifications = new Notifications();
        var requests = new List<string?>();
        var client = new Client(etag =>
        {
            requests.Add(etag);
            return etag is null ? new(false, "\"etag\"", Release) : new(true, "\"etag\"");
        });
        UpdateChecker Create() => new(client, store, notifications, new(1, 0, 0), clock);
        await Create().TickAsync(true);
        Assert.Single(requests);
        Assert.Single(notifications.Shown);
        clock.Advance(TimeSpan.FromHours(23));
        await Create().TickAsync(true);
        Assert.Single(requests);
        clock.Advance(TimeSpan.FromHours(1));
        await Create().TickAsync(true);
        Assert.Equal(new string?[] { null, "\"etag\"" }, requests);
        Assert.Equal(Release, store.State.Latest);
        Assert.Equal(2, notifications.Shown.Count);
    }

    [Fact]
    public async Task DisabledChecksDoNotReadStateOrContactNetworkAndCannotInstall()
    {
        var client = new Client(_ => throw new Exception("Network must not run"));
        var store = new StateStore();
        var checker = new UpdateChecker(client, store, new Notifications(), new(1, 0, 0));
        await checker.TickAsync(false);
        Assert.Equal(0, store.Reads);
        Assert.Null(await checker.ApplyActionAsync("install", Release.Tag, false));
    }

    [Fact]
    public async Task SnoozeAndIgnorePersistWhileNewVersionStillNotifies()
    {
        var clock = new Clock();
        var store = new StateStore();
        var notifications = new Notifications();
        var release = Release;
        var client = new Client(_ => new(false, "\"etag\"", release));
        UpdateChecker Create() => new(client, store, notifications, new(1, 0, 0), clock);
        await Create().TickAsync(true);
        await Create().ApplyActionAsync("later", release.Tag, true);
        clock.Advance(TimeSpan.FromHours(3.99));
        await Create().TickAsync(true);
        Assert.Single(notifications.Shown);
        clock.Advance(TimeSpan.FromHours(.01));
        await Create().TickAsync(true);
        Assert.Equal(2, notifications.Shown.Count);
        await Create().ApplyActionAsync("ignore", release.Tag, true);
        clock.Advance(TimeSpan.FromDays(1));
        await Create().TickAsync(true);
        Assert.Equal(2, notifications.Shown.Count);
        Assert.Null(await Create().ApplyActionAsync("install", release.Tag, true));
        release = release with { Tag = "v2.0.1", DownloadUrl = release.DownloadUrl.Replace("2.0.0", "2.0.1") };
        clock.Advance(TimeSpan.FromDays(1));
        await Create().TickAsync(true);
        Assert.Equal(release, notifications.Shown.Last());
        Assert.Null(await Create().ApplyActionAsync("install", "v2.0.0", true));
        Assert.Equal(release, await Create().ApplyActionAsync("install", release.Tag, true));
    }

    [Fact]
    public async Task FailedAttemptsArePersistedAndRateLimitCanExtendDeadline()
    {
        var clock = new Clock();
        var store = new StateStore();
        var client = new Client(_ => throw new HttpRequestException("offline"));
        UpdateChecker Create() => new(client, store, new Notifications(), new(1, 0, 0), clock);
        await Create().TickAsync(true);
        await Create().TickAsync(true);
        Assert.Equal(1, client.Calls);
        clock.Advance(TimeSpan.FromDays(1));
        client.GetResult = _ => new(false, RetryAt: clock.GetUtcNow().AddDays(3));
        await Create().TickAsync(true);
        Assert.Equal(clock.GetUtcNow().AddDays(3), store.State.NextCheckAt);
        clock.Advance(TimeSpan.FromDays(2));
        await Create().TickAsync(true);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ManualCheckBypassesDailyDeadlineAndIgnoredVersionButKeepsEtagAndPreferenceIndependent()
    {
        var clock = new Clock();
        var store = new StateStore();
        var notifications = new Notifications();
        var client = new Client(_ => new(false, "\"etag\"", Release));
        var checker = new UpdateChecker(client, store, notifications, new(1, 0, 0), clock);
        await checker.TickAsync(true);
        await checker.ApplyActionAsync("ignore", Release.Tag, true);
        await checker.TickAsync(false);
        client.GetResult = etag =>
        {
            Assert.Equal("\"etag\"", etag);
            return new(true);
        };
        var result = await checker.CheckNowAsync();
        Assert.Equal(Release, result.Release);
        Assert.Equal(2, client.Calls);
        Assert.Single(notifications.Shown);
        Assert.Contains(Release.Tag, store.State.IgnoredVersions!);
        await checker.TickAsync(true);
        Assert.Equal(2, client.Calls);
        Assert.Single(notifications.Shown);
    }

    [Fact]
    public async Task ManualCheckRespectsServerBackoffAcrossRestartEvenWhenShorterThanDailyInterval()
    {
        var clock = new Clock();
        var store = new StateStore();
        var retryAt = clock.GetUtcNow().AddHours(2);
        var client = new Client(_ => new(false, RetryAt: retryAt));
        UpdateChecker Create() => new(client, store, new Notifications(), new(1, 0, 0), clock);
        await Create().TickAsync(true);
        var result = await Create().CheckNowAsync();
        Assert.Equal(retryAt, result.RetryAt);
        Assert.Null(result.Release);
        Assert.Equal(1, client.Calls);
        clock.Advance(TimeSpan.FromHours(2));
        client.GetResult = _ => new(false, Release: Release);
        Assert.Equal(Release, (await Create().CheckNowAsync()).Release);
        Assert.Equal(2, client.Calls);
        Assert.Null(store.State.RetryNotBefore);
    }

    [Fact]
    public async Task ManualCheckReportsFailureInsteadOfInstallingStaleCacheAndRejectsInstalledVersion()
    {
        var clock = new Clock();
        var store = new StateStore();
        var client = new Client(_ => new(false, Release: Release));
        var checker = new UpdateChecker(client, store, new Notifications(), new(1, 0, 0), clock);
        await checker.TickAsync(true);
        client.GetResult = _ => throw new HttpRequestException("offline");
        await Assert.ThrowsAsync<HttpRequestException>(() => checker.CheckNowAsync());
        Assert.Equal(Release, store.State.Latest);
        client.GetResult = _ => new(true);
        Assert.Null((await new UpdateChecker(client, store, new Notifications(), new(2, 0, 0), clock).CheckNowAsync()).Release);
    }

    [Theory]
    [InlineData("v1.0.0", false)]
    [InlineData("v0.9.0", false)]
    [InlineData("v2.0.0-beta", false)]
    [InlineData("v02.0.0", false)]
    [InlineData("v1.0.1", true)]
    [InlineData("v10.0.0", true)]
    public void OnlyNewerStableVersionsAreOffered(string tag, bool expected)
    {
        var release = Release with { Tag = tag, DownloadUrl = $"https://github.com/frikst/SolidRecords/releases/download/{tag}/SolidRecords-{tag[1..]}-win-x64.msi" };
        Assert.Equal(expected, release.IsNewerThan(new(1, 0, 0, 0)));
    }

    [Fact]
    public async Task GitHubRequestUsesConditionalHeadersAndParsesExactMsiAndDigest()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/frikst/SolidRecords/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Equal("\"cached\"", request.Headers.IfNoneMatch.Single().ToString());
            Assert.Contains("SolidRecords", request.Headers.UserAgent.ToString());
            Assert.Equal("application/vnd.github+json", request.Headers.Accept.Single().MediaType);
            return JsonResponse();
        }));
        var result = await new GitHubReleaseClient(http).CheckAsync("\"cached\"", default);
        Assert.Equal(Release, result.Release);
        Assert.Equal("\"new\"", result.ETag);
        Assert.False((Release with { DownloadUrl = "https://other.example/setup.msi" }).IsValid);
        Assert.False((Release with { Size = 536870913 }).IsValid);
        Assert.False((Release with { Sha256 = "" }).IsValid);
    }

    [Theory]
    [InlineData(304)]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task GitHubHandlesBodylessResponsesAndRetryAfter(int status)
    {
        var retryAt = DateTimeOffset.UtcNow.AddDays(3);
        using var http = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = new(retryAt);
            return response;
        }));
        var result = await new GitHubReleaseClient(http).CheckAsync(null, default);
        Assert.Equal(status == 304, result.NotModified);
        Assert.Null(result.Release);
        if (status is 403 or 429) Assert.Equal(retryAt, result.RetryAt);
    }

    [Theory]
    [InlineData("prerelease", "true")]
    [InlineData("draft", "true")]
    [InlineData("tag_name", "\"v2.0.0-beta\"")]
    [InlineData("assets", "[]")]
    public async Task GitHubRejectsDraftPreviewAndMissingInstaller(string property, string value)
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await JsonResponse().Content.ReadAsStringAsync())!;
        json[property] = JsonSerializer.Deserialize<JsonElement>(value);
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(json)) }));
        Assert.Null((await new GitHubReleaseClient(http).CheckAsync(null, default)).Release);
    }

    [Fact]
    public void OldSettingsEnableChecksByDefaultAndNewSettingRoundTrips()
    {
        Assert.True(JsonSerializer.Deserialize<ApplicationSettings>("{\"MinimumActivitySeconds\":15}")!.CheckForUpdates);
        var settings = new ApplicationSettings(CheckForUpdates: false);
        Assert.Equal(settings, JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings)));
    }

    private static HttpResponseMessage JsonResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            tag_name = Release.Tag, draft = false, prerelease = false,
            assets = new[] { new { name = "SolidRecords-2.0.0-win-x64.msi", browser_download_url = Release.DownloadUrl, size = Release.Size, digest = "sha256:" + Release.Sha256 } }
        });
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Headers.ETag = new("\"new\"");
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
    private sealed class Client(Func<string?, ReleaseCheckResult> getResult) : IReleaseClient
    {
        public Func<string?, ReleaseCheckResult> GetResult { get; set; } = getResult;
        public int Calls { get; private set; }
        public Task<ReleaseCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(GetResult(etag));
        }
    }
    private sealed class StateStore : IUpdateStateStore
    {
        public UpdateState State { get; private set; } = new();
        public int Reads { get; private set; }
        public Task<UpdateState> ReadAsync(CancellationToken cancellationToken) { Reads++; return Task.FromResult(State); }
        public Task SaveAsync(UpdateState state, CancellationToken cancellationToken) { State = state; return Task.CompletedTask; }
    }
    private sealed class Notifications : IUpdateNotifier
    {
        public List<UpdateRelease> Shown { get; } = [];
        public void Show(UpdateRelease release) => Shown.Add(release);
        public void Clear() { }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
