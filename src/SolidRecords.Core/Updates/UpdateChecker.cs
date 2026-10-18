namespace SolidRecords.Core.Updates;

/// <summary>Serializes checks and user choices; persists deadlines across application restarts.</summary>
public sealed class UpdateChecker(IReleaseClient client, IUpdateStateStore store, IUpdateNotifier notifier,
    Version installedVersion, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateState? _state;

    public async Task TickAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!enabled) { notifier.Clear(); return; }
            _state ??= await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            var now = _clock.GetUtcNow();
            if (_state.NextCheckAt is null || now >= _state.NextCheckAt)
            {
                // Save before I/O, including failed/offline attempts, to avoid restart loops behind NAT.
                await SaveAsync(_state with { NextCheckAt = now.AddDays(1) }, cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await client.CheckAsync(_state.ETag, cancellationToken).ConfigureAwait(false);
                    var latest = result.NotModified ? _state.Latest : result.Release;
                    if (result.RetryAt is not null)
                    {
                        if (result.RetryAt > _state.NextCheckAt)
                            await SaveAsync(_state with { NextCheckAt = result.RetryAt }, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var changed = latest?.Tag != _state.Latest?.Tag;
                        await SaveAsync(_state with
                        {
                            ETag = result.ETag ?? (result.NotModified ? _state.ETag : null), Latest = latest,
                            NextNotificationAt = changed ? null : _state.NextNotificationAt
                        }, cancellationToken).ConfigureAwait(false);
                        if (changed) notifier.Clear();
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                    || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    // Network failures do not affect recording or reset the last valid cached release.
                }
            }
            if (!CanOffer(_state.Latest)) notifier.Clear();
            if (CanOffer(_state.Latest) && (_state.NextNotificationAt is null || now >= _state.NextNotificationAt))
            {
                notifier.Show(_state.Latest!);
                await SaveAsync(_state with { NextNotificationAt = now.AddDays(1) }, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<UpdateRelease?> ApplyActionAsync(string action, string tag, bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!enabled) return null;
            _state ??= await store.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (_state.Latest?.Tag != tag || !CanOffer(_state.Latest)) return null;
            switch (action)
            {
                case "install": return _state.Latest;
                case "later":
                    await SaveAsync(_state with { NextNotificationAt = _clock.GetUtcNow().AddHours(4) }, cancellationToken).ConfigureAwait(false);
                    break;
                case "ignore":
                    await SaveAsync(_state with { IgnoredVersions = [.. _state.IgnoredVersions ?? [], tag] }, cancellationToken).ConfigureAwait(false);
                    break;
                default: return null;
            }
            notifier.Clear();
            return null;
        }
        finally { _gate.Release(); }
    }

    private bool CanOffer(UpdateRelease? release) => release?.IsNewerThan(installedVersion) == true
        && !(_state!.IgnoredVersions ?? []).Contains(release.Tag, StringComparer.Ordinal);

    private async Task SaveAsync(UpdateState state, CancellationToken cancellationToken)
    {
        await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        _state = state;
    }
}
