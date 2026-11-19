namespace Assistant.Abstractions.Scheduling;

/// <summary>Stored status values of <c>scheduled_jobs.status</c>.</summary>
public static class JobStatuses
{
    public const string Scheduled = "scheduled";

    public const string Running = "running";

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";

    public const string Cancelled = "cancelled";
}

/// <summary>
/// A job claimed by one worker. <see cref="Attempts"/> already includes this claim.
/// </summary>
/// <param name="Id">Job id.</param>
/// <param name="Kind">Job kind.</param>
/// <param name="PayloadJson">JSON payload.</param>
/// <param name="Attempts">Attempt number after the claim (1-based).</param>
/// <param name="MaxAttempts">Maximum number of attempts.</param>
/// <param name="RunAt">Time the job was due.</param>
public sealed record ClaimedJob(
    Guid Id,
    string Kind,
    string PayloadJson,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset RunAt);

/// <summary>
/// Fenced write applied when a worker finishes a job it still owns.
/// </summary>
public abstract record JobCompletion
{
    /// <summary>The job succeeded.</summary>
    public sealed record Succeeded : JobCompletion;

    /// <summary>The job failed for good.</summary>
    /// <param name="Reason">Short reason, without user data.</param>
    public sealed record Failed(string Reason) : JobCompletion;

    /// <summary>Return the job to <c>scheduled</c> at <paramref name="RunAt"/>.</summary>
    /// <param name="RunAt">Next run time.</param>
    /// <param name="Reason">Short reason, without user data.</param>
    public sealed record Retry(DateTimeOffset RunAt, string Reason) : JobCompletion;

    /// <summary>
    /// Return the job to <c>scheduled</c> at <paramref name="RunAt"/> and undo the attempt the claim counted.
    /// </summary>
    /// <param name="RunAt">Next run time.</param>
    public sealed record Reschedule(DateTimeOffset RunAt) : JobCompletion;
}

/// <summary>
/// Durable job table. <c>now</c> is always supplied by the caller from <see cref="TimeProvider"/>;
/// implementations must not call the database clock.
/// </summary>
public interface IJobStore
{
    /// <summary>
    /// Inserts a job. An existing idempotency key is returned as-is: a scheduled row is left
    /// alone and a finished row is not revived.
    /// </summary>
    /// <param name="spec">Job specification.</param>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Id of the inserted or existing job.</returns>
    public ValueTask<Guid> InsertAsync(JobSpec spec, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Cancels a job that is still <c>scheduled</c>.
    /// </summary>
    /// <param name="idempotencyKey">Idempotency key.</param>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when a scheduled row was cancelled.</returns>
    public ValueTask<bool> CancelAsync(string idempotencyKey, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Moves a <c>scheduled</c> job to another time. Does not revive a finished job and does not touch a running one.
    /// </summary>
    /// <param name="idempotencyKey">Idempotency key.</param>
    /// <param name="runAt">New run time.</param>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when a scheduled row was updated.</returns>
    public ValueTask<bool> RescheduleAsync(
        string idempotencyKey,
        DateTimeOffset runAt,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due jobs with <c>FOR UPDATE SKIP LOCKED</c>.
    /// </summary>
    /// <param name="workerId">Worker fence token.</param>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="batchSize">Maximum rows to claim.</param>
    /// <param name="lease">How long the claim is valid.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Claimed jobs. Empty when none are due.</returns>
    public ValueTask<IReadOnlyList<ClaimedJob>> ClaimAsync(
        string workerId,
        DateTimeOffset now,
        int batchSize,
        TimeSpan lease,
        CancellationToken ct);

    /// <summary>
    /// Extends the lease when this worker still owns the job.
    /// </summary>
    /// <param name="jobId">Job id.</param>
    /// <param name="workerId">Worker fence token.</param>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="lease">New lease length from <paramref name="now"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the row was updated.</returns>
    public ValueTask<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken ct);

    /// <summary>
    /// Applies <paramref name="completion"/> only when <paramref name="workerId"/> still owns the job.
    /// </summary>
    /// <param name="jobId">Job id.</param>
    /// <param name="workerId">Worker fence token.</param>
    /// <param name="completion">Resulting state.</param>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>False when the fence missed (another worker took the job).</returns>
    public ValueTask<bool> CompleteAsync(
        Guid jobId,
        string workerId,
        JobCompletion completion,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// Returns <c>running</c> jobs whose lease expired to <c>scheduled</c>. The attempt already counted stays.
    /// </summary>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of jobs returned to the queue.</returns>
    public ValueTask<int> ReapAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Deletes old terminal jobs: succeeded and cancelled past <paramref name="succeededRetention"/>,
    /// failed past <paramref name="failedRetention"/>.
    /// </summary>
    /// <param name="now">Current time from <see cref="TimeProvider"/>.</param>
    /// <param name="succeededRetention">Age after which succeeded and cancelled rows are deleted.</param>
    /// <param name="failedRetention">Age after which failed rows are deleted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows deleted.</returns>
    public ValueTask<int> PurgeAsync(
        DateTimeOffset now,
        TimeSpan succeededRetention,
        TimeSpan failedRetention,
        CancellationToken ct);
}
