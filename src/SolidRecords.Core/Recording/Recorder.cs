using System.Threading.Channels;

namespace SolidRecords.Core.Recording;

/// <summary>Serializes capture and persistence; Stop drains all accepted observations.</summary>
public sealed class Recorder : IAsyncDisposable
{
    private sealed record Work(CaptureSignal? Signal, TaskCompletionSource? Completion = null, DateTimeOffset? Cutoff = null);
    private readonly IActivityStore _store;
    private readonly IActivitySource _source;
    private readonly TimeProvider _clock;
    private readonly RecordingEngine _engine = new();
    private readonly ActivityLogPolicy _activityLog;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Channel<Work> _queue = Channel.CreateUnbounded<Work>(new() { SingleReader = true });
    private readonly Task _writer;
    private volatile bool _accepting;
    private volatile bool _isRecording;
    private volatile Exception? _failure;

    public Recorder(IActivityStore store, IActivitySource source, TimeProvider? clock = null, ActivityTrackingOptions? options = null)
    {
        _store = store;
        _source = source;
        _clock = clock ?? TimeProvider.System;
        _activityLog = new(options);
        _writer = Task.Run(WriteLoopAsync);
    }

    public bool IsRecording => _isRecording;
    public string? Error => _failure?.Message;
    public event EventHandler? StateChanged;
    /// <summary>Raised after changed records have been committed and can be read by report consumers.</summary>
    public event EventHandler? RecordsChanged;

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_isRecording) return;
            if (_failure is not null) throw new InvalidOperationException("Úložisko zlyhalo. Reštartujte aplikáciu.", _failure);
            await _store.InitializeAsync();
            await SubmitAsync(SignalKind.RecordingStarted);
            _accepting = true;
            try
            {
                await _source.StartAsync(Publish);
                _isRecording = _failure is null;
                if (_failure is not null) throw new InvalidOperationException("Zápis záznamov zlyhal.", _failure);
            }
            catch
            {
                _accepting = false;
                await _source.StopAsync();
                await SubmitAsync(SignalKind.RecordingStopped);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            // Keep accepting until the source has stopped; then flush the queue.
            await _source.StopAsync();
            _accepting = false;
            if (_isRecording || _failure is not null) await SubmitAsync(SignalKind.RecordingStopped);
        }
        finally
        {
            _accepting = false;
            _isRecording = false;
            _lifecycle.Release();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task DeleteBeforeAsync(DateTimeOffset cutoff)
    {
        await _lifecycle.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_queue.Writer.TryWrite(new(null, completion, cutoff)))
                throw new ObjectDisposedException(nameof(Recorder));
            await completion.Task;
        }
        finally { _lifecycle.Release(); }
    }

    private void Publish(CaptureSignal signal)
    {
        if (_accepting && _failure is null) _queue.Writer.TryWrite(new(signal));
    }

    private Task SubmitAsync(SignalKind kind)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new(new(kind, _clock.GetUtcNow()), completion)))
            completion.SetException(new ObjectDisposedException(nameof(Recorder)));
        return completion.Task;
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync())
        {
            if (work.Cutoff is { } cutoff)
            {
                try
                {
                    await _store.DeleteBeforeAsync(cutoff);
                    RecordsChanged?.Invoke(this, EventArgs.Empty);
                    work.Completion!.SetResult();
                }
                catch (Exception ex) { work.Completion!.SetException(ex); }
                continue;
            }
            var signal = work.Signal!;
            try
            {
                if (_failure is not null) throw new InvalidOperationException("Zápis do databázy bol zastavený.", _failure);
                if (signal.Kind == SignalKind.CaptureFailed)
                    throw new InvalidOperationException(signal.Reason ?? "Zber udalostí zlyhal.");
                ProcessActivity? existingProcess = null;
                if (signal.Kind == SignalKind.ForegroundChanged && signal.Window is { ProcessStartTicks: { } ticks } window &&
                    _engine.RequiresProcessLookup(window))
                    existingProcess = await _store.FindProcessAsync(window.ProcessId, ticks);
                var batch = _activityLog.Apply(_engine.Apply(signal, existingProcess));
                if (signal.Icon is { } icon) batch = batch with { ApplicationIcons = [icon] };
                if (batch.ApplicationIcons.Count > 0 || batch.Events.Count > 0 || batch.Intervals.Count > 0 || batch.Processes.Count > 0 || batch.Windows.Count > 0 || batch.RemovedIntervalIds.Count > 0)
                {
                    await _store.SaveAsync(batch);
                    RecordsChanged?.Invoke(this, EventArgs.Empty);
                }
                work.Completion?.SetResult();
            }
            catch (Exception ex)
            {
                var firstFailure = _failure is null;
                _failure ??= ex;
                _isRecording = false;
                work.Completion?.SetException(ex);
                if (firstFailure) StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        finally
        {
            _queue.Writer.TryComplete();
            await _writer;
            await _source.DisposeAsync();
            _lifecycle.Dispose();
        }
    }
}
