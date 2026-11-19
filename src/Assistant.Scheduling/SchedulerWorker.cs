using System.Diagnostics;
using Assistant.Abstractions.Scheduling;
using Assistant.Scheduling.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Scheduling;

/// <summary>
/// Claims due jobs, runs one handler per scope, renews the lease, and stops without taking new work.
/// </summary>
public sealed class SchedulerWorker(
    IServiceScopeFactory scopes,
    JobWakeSignal wake,
    TimeProvider clock,
    IOptions<SchedulerOptions> options,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    private readonly Lock _gate = new();
    private readonly List<Task> _inFlight = [];
    private int _running;
    private string _workerId = "";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var name = Environment.MachineName;
        if (name.Length > 80)
        {
            name = name[..80];
        }

        _workerId = $"{name}:{Guid.NewGuid():N}";
        await SeedPurgeAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DrainAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    SchedulerLog.PulseFailed(logger, exception);
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await wake.WaitAsync(options.Value.PulseInterval, clock, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            await WaitForInFlightAsync();
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        await using (var scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var reaped = await store.ReapAsync(now, ct);
            if (reaped > 0)
            {
                SchedulerTelemetry.JobsReaped.Add(reaped);
            }
        }

        while (!ct.IsCancellationRequested)
        {
            int free;
            lock (_gate)
            {
                free = settings.MaxDegreeOfParallelism - _running;
            }

            if (free <= 0)
            {
                break;
            }

            var batchSize = Math.Min(settings.BatchSize, free);
            IReadOnlyList<ClaimedJob> claimed;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
                claimed = await store.ClaimAsync(_workerId, clock.GetUtcNow(), batchSize, settings.Lease, ct);
            }

            if (claimed.Count == 0)
            {
                wake.DiscardDue(clock.GetUtcNow());
                break;
            }

            SchedulerTelemetry.JobsClaimed.Add(claimed.Count);
            foreach (var job in claimed)
            {
                Start(job);
            }
        }
    }

    private void Start(ClaimedJob job)
    {
        var task = RunJobAsync(job);
        lock (_gate)
        {
            _running++;
            _inFlight.Add(task);
        }

        _ = task.ContinueWith(
            finished =>
            {
                _ = finished.Exception;
                lock (_gate)
                {
                    _running--;
                    _inFlight.Remove(finished);
                }

                wake.Kick();
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private async Task RunJobAsync(ClaimedJob job)
    {
        var settings = options.Value;
        using var renewStop = new CancellationTokenSource();
        using var handlerStop = new CancellationTokenSource();
        var leaseLost = 0;
        var renewTask = RenewLoopAsync(job.Id, settings, () =>
        {
            Interlocked.Exchange(ref leaseLost, 1);
            try
            {
                handlerStop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }, renewStop.Token);

        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var registry = scope.ServiceProvider.GetRequiredService<JobHandlerRegistry>();
            JobOutcome outcome;
            if (!registry.TryGet(job.Kind, out var handler))
            {
                outcome = new JobOutcome.Fail("unknown_kind");
            }
            else
            {
                var context = new JobContext(
                    job.Id,
                    job.Kind,
                    job.PayloadJson,
                    job.Attempts,
                    job.RunAt,
                    clock.GetUtcNow());
                try
                {
                    outcome = await handler.ExecuteAsync(context, handlerStop.Token);
                }
                catch (Exception exception) when (Volatile.Read(ref leaseLost) == 0)
                {
                    SchedulerLog.JobThrew(logger, job.Id, job.Kind, exception.GetType().Name);
                    outcome = new JobOutcome.Retry(exception.GetType().Name);
                }
            }

            if (Volatile.Read(ref leaseLost) != 0)
            {
                NoteFence(job.Id, job.Kind);
                return;
            }

            var now = clock.GetUtcNow();
            var completion = Map(job, outcome, now);
            var wrote = await store.CompleteAsync(job.Id, _workerId, completion, now, CancellationToken.None);
            if (!wrote)
            {
                NoteFence(job.Id, job.Kind);
                return;
            }

            NoteCompletion(job.Kind, completion);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref leaseLost) != 0)
        {
            NoteFence(job.Id, job.Kind);
        }
        finally
        {
            SchedulerTelemetry.JobDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("kind", job.Kind));
            await renewStop.CancelAsync();
            try
            {
                await renewTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RenewLoopAsync(
        Guid jobId,
        SchedulerOptions settings,
        Action onLost,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(settings.LeaseRenewalInterval, clock, ct);
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
                var renewed = await store.RenewLeaseAsync(jobId, _workerId, clock.GetUtcNow(), settings.Lease, ct);
                if (!renewed)
                {
                    onLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task WaitForInFlightAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            pending = [.. _inFlight];
        }

        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(options.Value.ShutdownGrace);
        }
        catch (TimeoutException)
        {
            SchedulerLog.ShutdownLeftJobs(logger, pending.Length);
        }
    }

    private async Task SeedPurgeAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
            var settings = options.Value;
            var now = clock.GetUtcNow();
            var slot = PurgeSchedule.TodaySlot(now, settings);
            var spec = new JobSpec(
                SchedulerPurgeHandler.KindName,
                slot,
                "{}",
                PurgeSchedule.KeyFor(slot, settings));
            await scheduler.ScheduleAsync(spec, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SchedulerLog.SeedFailed(logger, exception);
        }
    }

    private static JobCompletion Map(ClaimedJob job, JobOutcome outcome, DateTimeOffset now)
        => outcome switch
        {
            JobOutcome.Succeeded => new JobCompletion.Succeeded(),
            JobOutcome.Fail fail => new JobCompletion.Failed(fail.Reason),
            JobOutcome.Reschedule again => new JobCompletion.Reschedule(again.RunAt),
            JobOutcome.Retry retry => MapRetry(job, retry, now),
            _ => throw new InvalidOperationException("Unknown job outcome."),
        };

    private static JobCompletion MapRetry(ClaimedJob job, JobOutcome.Retry retry, DateTimeOffset now)
    {
        var delay = BackoffPolicy.DelayAfterAttempt(job.Attempts, job.MaxAttempts, retry.After);
        if (delay is null)
        {
            return new JobCompletion.Failed(retry.Reason);
        }

        return new JobCompletion.Retry(now + delay.Value, retry.Reason);
    }

    private static void NoteCompletion(string kind, JobCompletion completion)
    {
        var outcome = completion switch
        {
            JobCompletion.Succeeded => "succeeded",
            JobCompletion.Failed => "failed",
            JobCompletion.Retry => "retry",
            JobCompletion.Reschedule => "reschedule",
            _ => "unknown",
        };
        var tags = new KeyValuePair<string, object?>("kind", kind);
        switch (completion)
        {
            case JobCompletion.Failed:
                SchedulerTelemetry.JobsFailed.Add(1, tags);
                break;
            case JobCompletion.Retry:
                SchedulerTelemetry.JobsRetried.Add(1, tags);
                break;
            default:
                SchedulerTelemetry.JobsCompleted.Add(1, tags, new KeyValuePair<string, object?>("outcome", outcome));
                break;
        }
    }

    private void NoteFence(Guid jobId, string kind)
    {
        SchedulerLog.FenceRejected(logger, jobId, kind);
        SchedulerTelemetry.FenceRejected.Add(1, new KeyValuePair<string, object?>("kind", kind));
    }
}

internal static partial class SchedulerLog
{
    [LoggerMessage(EventId = 770, Level = LogLevel.Error, Message = "Scheduler pulse failed")]
    public static partial void PulseFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 771,
        Level = LogLevel.Error,
        Message = "Job {JobId} ({Kind}) threw {ExceptionType}")]
    public static partial void JobThrew(ILogger logger, Guid jobId, string kind, string exceptionType);

    [LoggerMessage(
        EventId = 772,
        Level = LogLevel.Warning,
        Message = "Discarded the result of job {JobId} ({Kind}); the lease fence missed")]
    public static partial void FenceRejected(ILogger logger, Guid jobId, string kind);

    [LoggerMessage(
        EventId = 773,
        Level = LogLevel.Warning,
        Message = "Shutdown left {Count} job(s) running; the lease will be reaped")]
    public static partial void ShutdownLeftJobs(ILogger logger, int count);

    [LoggerMessage(EventId = 774, Level = LogLevel.Error, Message = "Failed to seed scheduler.purge")]
    public static partial void SeedFailed(ILogger logger, Exception exception);
}
