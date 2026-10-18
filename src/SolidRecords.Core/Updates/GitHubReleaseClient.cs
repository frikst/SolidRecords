using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SolidRecords.Core.Updates;

public sealed class GitHubReleaseClient(HttpClient http) : IReleaseClient
{
    public async Task<ReleaseCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        cancellationToken = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/frikst/SolidRecords/releases/latest");
        request.Headers.UserAgent.ParseAdd("SolidRecords/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (EntityTagHeaderValue.TryParse(etag, out var parsed)) request.Headers.IfNoneMatch.Add(parsed);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var newETag = response.Headers.ETag?.ToString();
        if (response.StatusCode == HttpStatusCode.NotModified) return new(true, newETag);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            var now = DateTimeOffset.UtcNow;
            var retryAt = response.Headers.RetryAfter?.Date ?? now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromDays(1));
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                && long.TryParse(values.FirstOrDefault(), out var reset) && reset is >= 0 and <= 253402300799)
            {
                var resetAt = DateTimeOffset.FromUnixTimeSeconds(reset);
                if (resetAt > retryAt) retryAt = resetAt;
            }
            return new(false, RetryAt: retryAt);
        }
        if (response.StatusCode == HttpStatusCode.NotFound) return new(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = json.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String
            || root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
            || root.TryGetProperty("prerelease", out var preview) && preview.ValueKind == JsonValueKind.True)
            return new(false, newETag);
        var tag = tagElement.GetString()!;
        if (UpdateRelease.ParseVersion(tag) is null || !root.TryGetProperty("assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array) return new(false, newETag);
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != $"SolidRecords-{tag[1..]}-win-x64.msi"
                || !asset.TryGetProperty("browser_download_url", out var url) || url.ValueKind != JsonValueKind.String
                || !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var length)
                || !asset.TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String) continue;
            var hash = digest.GetString()!;
            if (!hash.StartsWith("sha256:", StringComparison.Ordinal)) continue;
            var release = new UpdateRelease(tag, url.GetString()!, length, hash[7..]);
            if (release.IsValid) return new(false, newETag, release);
        }
        return new(false, newETag);
    }
}
