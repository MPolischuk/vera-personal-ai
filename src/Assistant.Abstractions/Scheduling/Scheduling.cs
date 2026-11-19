namespace Assistant.Abstractions.Scheduling;

/// <summary>
/// Specification of a job to schedule.
/// </summary>
/// <param name="Kind">Job kind (for example, "reminder.fire", "notification.dispatch").</param>
/// <param name="RunAt">Run time (UTC).</param>
/// <param name="PayloadJson">Job JSON payload.</param>
/// <param name="IdempotencyKey">Idempotency key (unique): scheduling the same job again does not duplicate it.</param>
/// <param name="MaxAttempts">Maximum number of attempts.</param>
public sealed record JobSpec(
    string Kind,
    DateTimeOffset RunAt,
    string PayloadJson,
    string? IdempotencyKey = null,
    int MaxAttempts = 5);

/// <summary>
/// Outcome of running a job. A closed record hierarchy.
/// </summary>
public abstract record JobOutcome
{
    /// <summary>Ran successfully.</summary>
    public sealed record Succeeded : JobOutcome;

    /// <summary>Retry with backoff.</summary>
    /// <param name="Reason">Why the job should be retried.</param>
    /// <param name="After">Delay before retrying (null means the standard backoff).</param>
    public sealed record Retry(string Reason, TimeSpan? After = null) : JobOutcome;

    /// <summary>Reschedule for another time (does not count as a failed attempt).</summary>
    /// <param name="RunAt">New run time.</param>
    public sealed record Reschedule(DateTimeOffset RunAt) : JobOutcome;

    /// <summary>Definitive failure; do not retry.</summary>
    /// <param name="Reason">Why the job failed.</param>
    public sealed record Fail(string Reason) : JobOutcome;
}

/// <summary>
/// Execution context of a job.
/// </summary>
/// <param name="JobId">Job id.</param>
/// <param name="Kind">Job kind.</param>
/// <param name="PayloadJson">JSON payload.</param>
/// <param name="Attempt">Attempt number (1-based).</param>
/// <param name="ScheduledFor">Time the job was scheduled for.</param>
/// <param name="Now">Current execution time.</param>
public sealed record JobContext(
    Guid JobId,
    string Kind,
    string PayloadJson,
    int Attempt,
    DateTimeOffset ScheduledFor,
    DateTimeOffset Now);

/// <summary>
/// Handler for one job kind.
/// </summary>
public interface IJobHandler
{
    /// <summary>Job kind this handler runs.</summary>
    public string Kind { get; }

    /// <summary>
    /// Runs the job.
    /// </summary>
    /// <param name="context">Job context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Execution outcome.</returns>
    public ValueTask<JobOutcome> ExecuteAsync(JobContext context, CancellationToken ct);
}

/// <summary>
/// Durable job scheduler.
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// Schedules a job (idempotent by key).
    /// </summary>
    /// <param name="spec">Job specification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Id of the job (existing or new).</returns>
    public ValueTask<Guid> ScheduleAsync(JobSpec spec, CancellationToken ct);

    /// <summary>
    /// Cancels a scheduled job (only when it is not running).
    /// </summary>
    /// <param name="idempotencyKey">Idempotency key of the job.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when it was cancelled; false when it did not exist or was already running.</returns>
    public ValueTask<bool> CancelAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Reschedules an existing job for another time.
    /// </summary>
    /// <param name="idempotencyKey">Idempotency key of the job.</param>
    /// <param name="runAt">New run time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when it was rescheduled; false when it did not exist or was already running.</returns>
    public ValueTask<bool> RescheduleAsync(string idempotencyKey, DateTimeOffset runAt, CancellationToken ct);
}
