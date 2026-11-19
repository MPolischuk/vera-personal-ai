namespace Assistant.Extensions.Memory;

/// <summary>
/// When to sweep: every <see cref="StoredThreshold"/> <c>MemoryStored</c> events for a user,
/// or at the next local midnight in <see cref="TimeZoneId"/>, whichever comes first.
/// Counting does not call a model.
/// </summary>
internal sealed class MemoryConsolidationSchedule
{
    public const int StoredThreshold = 5;

    public const string TimeZoneId = "America/Argentina/Buenos_Aires";

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, int> _counts = [];
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    private DateOnly? _lastDailyRun;
    private TaskCompletionSource _pulse = NewPulse();

    public void NoteStored(Guid userId)
    {
        lock (_gate)
        {
            _counts.TryGetValue(userId, out var count);
            count++;
            _counts[userId] = count;
            if (count % StoredThreshold == 0)
            {
                _pulse.TrySetResult();
            }
        }
    }

    public MemoryConsolidationDue? TryConsume(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            var localDate = LocalDate(utcNow);
            _lastDailyRun ??= localDate;
            var daily = localDate > _lastDailyRun.Value;
            var users = _counts
                .Where(pair => pair.Value >= StoredThreshold)
                .Select(pair => pair.Key)
                .ToArray();
            if (!daily && users.Length == 0)
            {
                return null;
            }

            if (daily)
            {
                _lastDailyRun = localDate;
                _counts.Clear();
            }
            else
            {
                foreach (var userId in users)
                {
                    _counts[userId] %= StoredThreshold;
                }
            }

            return new MemoryConsolidationDue(daily, users);
        }
    }

    public TimeSpan UntilNextLocalMidnight(DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, _zone);
        var nextLocal = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, _zone);
        var delay = nextUtc - utcNow;
        return delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delay;
    }

    public async Task WaitAsync(TimeSpan delay, TimeProvider clock, CancellationToken ct)
    {
        Task pulse;
        lock (_gate)
        {
            if (_pulse.Task.IsCompleted)
            {
                _pulse = NewPulse();
                return;
            }

            pulse = _pulse.Task;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delayTask = Task.Delay(delay, clock, linked.Token);
        var finished = await Task.WhenAny(delayTask, pulse).ConfigureAwait(false);
        if (pulse.IsCompleted)
        {
            await linked.CancelAsync().ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            if (ReferenceEquals(finished, delayTask) || pulse.IsCompleted)
            {
                await delayTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }

    private DateOnly LocalDate(DateTimeOffset utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, _zone).DateTime);

    private static TaskCompletionSource NewPulse()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record MemoryConsolidationDue(bool Daily, IReadOnlyList<Guid> Users);
