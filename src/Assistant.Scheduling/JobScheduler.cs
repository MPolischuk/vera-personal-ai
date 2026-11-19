using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Options;

namespace Assistant.Scheduling;

/// <summary>
/// In-process wake-up. A schedule whose <c>RunAt</c> falls inside the pulse records the earliest
/// time and releases the worker so it does not sleep the whole interval.
/// </summary>
public sealed class JobWakeSignal : IDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _ready = new(0, 1);
    private DateTimeOffset? _earliest;
    private bool _disposed;

    public void Notify(DateTimeOffset runAt)
    {
        lock (_gate)
        {
            if (_earliest is null || runAt < _earliest)
            {
                _earliest = runAt;
            }
        }

        Release();
    }

    public void Kick() => Release();

    public void DiscardDue(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_earliest is { } due && due <= now)
            {
                _earliest = null;
            }
        }
    }

    public async Task WaitAsync(TimeSpan maximum, TimeProvider clock, CancellationToken ct)
    {
        var delay = maximum;
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_earliest is { } next && next > now)
            {
                var until = next - now;
                if (until < delay)
                {
                    delay = until;
                }
            }
        }

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timer = Task.Delay(delay, clock, linked.Token);
        var signal = _ready.WaitAsync(ct);
        var finished = await Task.WhenAny(timer, signal);
        if (!ReferenceEquals(finished, timer))
        {
            await linked.CancelAsync();
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            await timer;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ready.Dispose();
    }

    private void Release()
    {
        try
        {
            _ready.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

/// <summary>
/// Schedules through the scoped <see cref="IJobStore"/> so an ambient transaction is joined.
/// </summary>
public sealed class JobScheduler(
    IJobStore store,
    JobWakeSignal wake,
    TimeProvider clock,
    IOptions<SchedulerOptions> options) : IJobScheduler
{
    public async ValueTask<Guid> ScheduleAsync(JobSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var now = clock.GetUtcNow();
        var id = await store.InsertAsync(spec, now, ct);
        var pulse = options.Value.PulseInterval;
        if (spec.RunAt <= now + pulse)
        {
            wake.Notify(spec.RunAt);
        }

        return id;
    }

    public async ValueTask<bool> CancelAsync(string idempotencyKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return await store.CancelAsync(idempotencyKey, clock.GetUtcNow(), ct);
    }

    public async ValueTask<bool> RescheduleAsync(
        string idempotencyKey,
        DateTimeOffset runAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var now = clock.GetUtcNow();
        var updated = await store.RescheduleAsync(idempotencyKey, runAt, now, ct);
        if (updated && runAt <= now + options.Value.PulseInterval)
        {
            wake.Notify(runAt);
        }

        return updated;
    }
}
