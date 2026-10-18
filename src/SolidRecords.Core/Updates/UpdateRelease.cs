using System.Text.RegularExpressions;

namespace SolidRecords.Core.Updates;

public sealed record UpdateRelease(string Tag, string DownloadUrl, long Size, string Sha256)
{
    public static Version? ParseVersion(string tag) => !string.IsNullOrEmpty(tag) && tag.Length <= 32 && Regex.IsMatch(tag, @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")
        && Version.TryParse(tag[1..], out var version) ? version : null;

    public bool IsValid => ParseVersion(Tag) is not null
        && Size is > 0 and <= 536870912
        && Sha256 is not null && Regex.IsMatch(Sha256, "^[a-fA-F0-9]{64}$")
        && DownloadUrl == $"https://github.com/frikst/SolidRecords/releases/download/{Tag}/SolidRecords-{Tag[1..]}-win-x64.msi";

    public bool IsNewerThan(Version installed) => IsValid && ParseVersion(Tag)! > new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
}

public sealed record UpdateState(DateTimeOffset? NextCheckAt = null, string? ETag = null,
    UpdateRelease? Latest = null, DateTimeOffset? NextNotificationAt = null, string[]? IgnoredVersions = null);

public interface IUpdateStateStore
{
    Task<UpdateState> ReadAsync(CancellationToken cancellationToken);
    Task SaveAsync(UpdateState state, CancellationToken cancellationToken);
}

public sealed record ReleaseCheckResult(bool NotModified, string? ETag = null, UpdateRelease? Release = null,
    DateTimeOffset? RetryAt = null);

public interface IReleaseClient
{
    Task<ReleaseCheckResult> CheckAsync(string? etag, CancellationToken cancellationToken);
}

public interface IUpdateNotifier
{
    void Show(UpdateRelease release);
    void Clear();
}
